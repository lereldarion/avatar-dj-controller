//! Prototype

use std::u32;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let args = <Args as clap::Parser>::parse();

    match capture_session(&args) {
        CaptureEnd::Closed => eprintln!("{} has closed", args.window_name),
        CaptureEnd::WindowNotFound => eprintln!("{} not found", args.window_name),
        CaptureEnd::Error(e) => return Err(e),
    }

    Ok(())
}

#[derive(Debug, clap::Parser)]
#[command(version, about, long_about = None)]
struct Args {
    /// Name of the window to capture
    #[arg(short, long, id = "name", default_value = "VRChat")]
    window_name: String,

    /// Maximum pixel distance to search midi pixel pattern, from bottom left of window
    #[arg(long, id = "pixels", default_value = "20")]
    offset_search_limit: u32,
}

enum CaptureEnd {
    WindowNotFound,
    Closed,
    Error(Box<dyn std::error::Error + Sync + Send>),
}

fn capture_session(args: &Args) -> CaptureEnd {
    let window = match windows_capture::window::Window::from_name(&args.window_name) {
        Ok(window) => window,
        Err(windows_capture::window::Error::NotFound(_)) => return CaptureEnd::WindowNotFound,
        Err(windows_capture::window::Error::WindowsError(e))
            if e.code() == windows_result::HRESULT(0) =>
        {
            // Defect: windows rust API for FindWindowW returns an empty error for "not found"
            return CaptureEnd::WindowNotFound;
        }
        Err(e) => return CaptureEnd::Error(Box::new(e)),
    };

    let settings = windows_capture::settings::Settings::new(
        window,
        windows_capture::settings::CursorCaptureSettings::WithoutCursor,
        windows_capture::settings::DrawBorderSettings::Default,
        windows_capture::settings::ColorFormat::Rgba8,
        args.offset_search_limit,
    );

    match <CaptureCallbackState as windows_capture::capture::GraphicsCaptureApiHandler>::start(
        settings,
    ) {
        Ok(()) => CaptureEnd::Closed,
        Err(e) => CaptureEnd::Error(Box::new(e)),
    }
}

struct CaptureCallbackState {
    last_frame_size: Vec2<u32>,
    last_frame_time: std::time::Instant,
    midi: MidiState,
    /// Offset to skip window decorations (border).
    /// To be computed by finding the fixed pattern.
    pixel_offset: Vec2<u32>,
    offset_search_limit: u32,
}

impl windows_capture::capture::GraphicsCaptureApiHandler for CaptureCallbackState {
    type Flags = u32;

    type Error = Box<dyn std::error::Error + Sync + Send>;

    fn new(ctx: windows_capture::capture::Context<Self::Flags>) -> Result<Self, Self::Error> {
        Ok(CaptureCallbackState {
            last_frame_size: Vec2::MAX,
            last_frame_time: std::time::Instant::now(),
            midi: MidiState::new(),
            pixel_offset: Vec2::MAX,
            offset_search_limit: ctx.flags,
        })
    }

    fn on_frame_arrived(
        &mut self,
        frame: &mut windows_capture::frame::Frame,
        _capture_control: windows_capture::graphics_capture_api::InternalCaptureControl,
    ) -> Result<(), Self::Error> {
        // Find offset to midi pixel block before doing anything.
        // This block is placed at bottom left of VRChat window, but capture of window takes the window border.
        // This step will look for a specific pattern from the bottom left to detect the window border size.
        // Window border size should not change, so once detected stop searching.
        if self.pixel_offset == Vec2::MAX {
            let mut buffer = frame.buffer()?;
            let pixels = PixelBuffer::try_from(&mut buffer)?;
            if let Some(offset) = pixels.find_offset_tag(self.offset_search_limit) {
                self.pixel_offset = offset;
                eprintln!("Found midi pixel output offset: {offset}");
            } else {
                return Ok(()); // Wait next frame
            }
        }

        // Ensure minimum frame size
        let requested_frame_size = Vec2::new(1 + 128, 16) + self.pixel_offset;
        let frame_size = Vec2::new(frame.width(), frame.height());
        let last_frame_size = std::mem::replace(&mut self.last_frame_size, frame_size);
        match (
            last_frame_size.contains(&requested_frame_size),
            frame_size.contains(&requested_frame_size),
        ) {
            (true, true) => (),
            (false, true) => eprintln!("Resuming midi decoding"),
            (true, false) => {
                eprintln!("Pausing midi decoding, window must be at least {requested_frame_size}");
                return Ok(());
            }
            (false, false) => return Ok(()),
        };

        let mut midi_pixel_block = frame.buffer_crop(
            self.pixel_offset.x,
            frame_size.y - requested_frame_size.y,
            requested_frame_size.x,
            frame_size.y - self.pixel_offset.y,
        )?;
        let buffer = PixelBuffer::try_from(&mut midi_pixel_block)?;

        let mut debug = String::new();
        for controller_id in 0..128 {
            if let Some(value_u7) = buffer.decode_u7_with_check(Vec2::new(controller_id + 1, 0)) {
                let old = std::mem::replace(
                    &mut self.midi.controllers[controller_id as usize],
                    Some(value_u7),
                );
                if old != Some(value_u7) {
                    // TODO send midi update
                }
                debug = format!("{debug}{:>3}:{:>3}; ", controller_id, value_u7);
            }
        }

        // Debug
        let now = std::time::Instant::now();
        let last_frame_time = std::mem::replace(&mut self.last_frame_time, now);
        let elapsed = now.duration_since(last_frame_time);
        let frame_time = elapsed.as_secs_f64();
        eprintln!("{debug}dt={:.1}ms", frame_time * 1000.);

        Ok(())
    }
}

#[derive(Debug)]
struct MidiState {
    controllers: [Option<u8>; 128],
}

impl MidiState {
    fn new() -> Self {
        MidiState {
            controllers: [None; 128],
        }
    }
}

struct PixelBuffer<'b> {
    size: Vec2<u32>,
    row_pitch: u32,
    buffer: &'b [u8],
}

impl<'b> TryFrom<&'b mut windows_capture::frame::FrameBuffer<'_>> for PixelBuffer<'b> {
    type Error = windows_capture::frame::Error;
    fn try_from(
        buffer: &'b mut windows_capture::frame::FrameBuffer<'_>,
    ) -> Result<Self, Self::Error> {
        let size = Vec2::new(buffer.width(), buffer.height());
        let row_pitch = buffer.row_pitch();
        let buffer = buffer.as_raw_buffer();
        Ok(PixelBuffer {
            size,
            row_pitch,
            buffer: &*buffer,
        })
    }
}

impl<'b> PixelBuffer<'b> {
    fn pixel(&self, position: Vec2<u32>) -> Pixel {
        let y = self.size.y - 1 - position.y; // DX11 is from top
        let offset = position.x * 4 + y * self.row_pitch;
        let offset = offset as usize;
        let p = &self.buffer[offset..offset + 4];
        Pixel([p[0], p[1], p[2], p[3]])
    }

    fn decode_u14(&self, position: Vec2<u32>) -> Option<u16> {
        let black = self.pixel(position);
        let white = self.pixel(position + Vec2::new(0, 1));
        if Pixel::distance(&black, &white) < u8::MAX / 2 {
            return None; // Black and white should be very different
        }

        let decode_bit = |y: u32| -> Option<bool> {
            let pixel = self.pixel(position + Vec2::new(0, y));
            let black_distance = Pixel::distance(&black, &pixel);
            let white_distance = Pixel::distance(&white, &pixel);
            if u8::min(black_distance, white_distance) >= u8::MAX / 4 {
                return None; // Not significant enough
            }
            Some(white_distance < black_distance)
        };

        let mut value: u16 = 0;
        for i in 0..14 {
            let bit = decode_bit(2 + i)?;
            if bit {
                value |= 0b1 << i;
            }
        }

        Some(value)
    }

    fn decode_u7_with_check(&self, position: Vec2<u32>) -> Option<u8> {
        let u7_followed_by_inverse_bits = self.decode_u14(position)?;
        let mask: u16 = (1 << 7) - 1;
        let value = u7_followed_by_inverse_bits & mask;
        let value_from_inverse = (std::ops::Not::not(u7_followed_by_inverse_bits) >> 7) & mask;
        if value == value_from_inverse {
            Some(value as u8)
        } else {
            None
        }
    }

    /// Triangular search
    fn find_offset_tag(&self, limit: u32) -> Option<Vec2<u32>> {
        for sum in 0..limit {
            for x in 0..=sum {
                let y = sum - x;
                if self.decode_u14(Vec2 { x, y }) == Some(0x2AAA) {
                    return Some(Vec2 { x, y });
                }
            }
        }
        None
    }
}

/// RGBA, but order is not that important
#[derive(Debug)]
struct Pixel([u8; 4]);

impl Pixel {
    fn distance(lhs: &Self, rhs: &Self) -> u8 {
        let distances = [
            i16::abs(lhs.0[0] as i16 - rhs.0[0] as i16),
            i16::abs(lhs.0[1] as i16 - rhs.0[1] as i16),
            i16::abs(lhs.0[2] as i16 - rhs.0[2] as i16),
            i16::abs(lhs.0[3] as i16 - rhs.0[3] as i16),
        ];
        let sum_distance: i16 = distances.iter().sum();
        (sum_distance / 4) as u8
    }
}

#[derive(Debug, Default, Clone, Copy, PartialEq, Eq)]
struct Vec2<T> {
    x: T,
    y: T,
}
impl<T> Vec2<T> {
    const fn new(x: T, y: T) -> Self {
        Vec2 { x, y }
    }
}
impl<T: Ord> Vec2<T> {
    fn contains(&self, other: &Vec2<T>) -> bool {
        self.x >= other.x && self.y >= other.y
    }
}
impl<T: std::ops::Add> std::ops::Add for Vec2<T> {
    type Output = Vec2<T::Output>;
    fn add(self, rhs: Self) -> Self::Output {
        Vec2::new(self.x + rhs.x, self.y + rhs.y)
    }
}
impl<T: std::fmt::Display> std::fmt::Display for Vec2<T> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}x{}", self.x, self.y)
    }
}
impl Vec2<u32> {
    const MAX: Self = Vec2::new(u32::MAX, u32::MAX);
}
