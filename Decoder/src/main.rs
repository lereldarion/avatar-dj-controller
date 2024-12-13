//! Prototype

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let args = <Args as clap::Parser>::parse();

    match capture_session(&args.window_name) {
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
    #[arg(short, long, default_value = "VRChat")]
    window_name: String,
}

enum CaptureEnd {
    WindowNotFound,
    Closed,
    Error(Box<dyn std::error::Error + Sync + Send>),
}

fn capture_session(window_name: &str) -> CaptureEnd {
    let window = match windows_capture::window::Window::from_name(window_name) {
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
        (),
    );

    match <CaptureCallbackState as windows_capture::capture::GraphicsCaptureApiHandler>::start(
        settings,
    ) {
        Ok(()) => CaptureEnd::Closed,
        Err(e) => CaptureEnd::Error(Box::new(e)),
    }
}

struct CaptureCallbackState {
    last_frame_size: Size<u32>,
    last_frame_time: std::time::Instant,
    midi: MidiState,
}

impl windows_capture::capture::GraphicsCaptureApiHandler for CaptureCallbackState {
    type Flags = ();

    type Error = Box<dyn std::error::Error + Sync + Send>;

    fn new(_ctx: windows_capture::capture::Context<Self::Flags>) -> Result<Self, Self::Error> {
        Ok(CaptureCallbackState {
            last_frame_size: Size::new(u32::MAX, u32::MAX),
            last_frame_time: std::time::Instant::now(),
            midi: MidiState::new(),
        })
    }

    fn on_frame_arrived(
        &mut self,
        frame: &mut windows_capture::frame::Frame,
        capture_control: windows_capture::graphics_capture_api::InternalCaptureControl,
    ) -> Result<(), Self::Error> {
        // Timing debug
        let now = std::time::Instant::now();
        let last_frame_time = std::mem::replace(&mut self.last_frame_time, now);
        let elapsed = now.duration_since(last_frame_time);
        let frame_time = elapsed.as_secs_f64();

        // Ensure minimum frame size
        let requested_frame_size = Size::new(128, 16);
        let frame_size = Size::new(frame.width(), frame.height());
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

        if last_frame_size.x == u32::MAX {
            frame.save_as_image(r"test.png", windows_capture::frame::ImageFormat::Png)?;
        }

        //let mut cropped_framebuffer =frame.buffer_crop(0, 0, requested_frame_size.x, requested_frame_size.y)?;
        let mut cropped_framebuffer = frame.buffer()?;
        let buffer = PixelBuffer::try_from(&mut cropped_framebuffer)?;

        for controller_id in 0..128 {
            if let Some(value_u7) = buffer.decode_u7(controller_id) {
                match &mut self.midi.controllers[controller_id] {
                    place @ None => {
                        eprintln!("Add controller[{controller_id}] = {value_u7}");
                        *place = Some(value_u7)
                    }
                    Some(place) => {
                        if *place != value_u7 {
                            eprintln!("Update controller[{controller_id}] = {value_u7}");
                            *place = value_u7;
                        }
                    }
                }
            }
        }

        if false {
            eprintln!(
                "dt={:.1}ms, FPS={:.0}, size={frame_size}",
                frame_time * 1000.,
                1. / frame_time
            );
        }

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
    width: usize,
    height: usize,
    row_pitch: usize,
    buffer: &'b [u8],
}

impl<'b> TryFrom<&'b mut windows_capture::frame::FrameBuffer<'_>> for PixelBuffer<'b> {
    type Error = windows_capture::frame::Error;
    fn try_from(
        buffer: &'b mut windows_capture::frame::FrameBuffer<'_>,
    ) -> Result<Self, Self::Error> {
        let width = buffer.width() as usize;
        let height = buffer.height() as usize;
        let row_pitch = buffer.row_pitch() as usize;
        let buffer = buffer.as_raw_buffer();
        Ok(PixelBuffer {
            width,
            height,
            row_pitch,
            buffer: &*buffer,
        })
    }
}

impl<'b> PixelBuffer<'b> {
    fn pixel(&self, x: usize, y: usize) -> Pixel {
        let (x, y) = (x + 1, y + 1); // FIXME border is included, find a way to exclude from capture later
        let y = self.height - 1 - y; // DX11 is from top

        let offset = x * 4 + y * self.row_pitch;
        let p = &self.buffer[offset..offset + 4];
        Pixel([p[0], p[1], p[2], p[3]])
    }

    fn decode_u7(&self, x: usize) -> Option<u8> {
        let black = self.pixel(x, 0);
        let white = self.pixel(x, 1);
        if Pixel::distance(&black, &white) < u8::MAX / 2 {
            return None; // Black and white should be very different
        }

        let decode_bit = |y: usize| -> Option<bool> {
            let pixel = self.pixel(x, y);
            let black_distance = Pixel::distance(&black, &pixel);
            let white_distance = Pixel::distance(&white, &pixel);
            if u8::min(black_distance, white_distance) >= u8::MAX / 4 {
                return None; // Not significant enough
            }
            Some(white_distance < black_distance)
        };

        let mut value: u8 = 0;
        for i in 0..7 {
            let bit = decode_bit(2 + i)?;
            let inverse = decode_bit(2 + 7 + i)?;
            if bit != !inverse {
                return None;
            }
            if bit {
                value |= 0b1 << i;
            }
        }

        Some(value)
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

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
struct Size<T> {
    x: T,
    y: T,
}

impl<T> Size<T> {
    fn new(x: T, y: T) -> Self {
        Size { x, y }
    }
}

impl<T: Ord> Size<T> {
    fn contains(&self, other: &Size<T>) -> bool {
        self.x >= other.x && self.y >= other.y
    }
}

impl<T: std::fmt::Display> std::fmt::Display for Size<T> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}x{}", self.x, self.y)
    }
}
