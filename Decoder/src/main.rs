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
    last_frame: std::time::Instant,
}

impl windows_capture::capture::GraphicsCaptureApiHandler for CaptureCallbackState {
    type Flags = ();

    type Error = Box<dyn std::error::Error + Sync + Send>;

    fn new(_ctx: windows_capture::capture::Context<Self::Flags>) -> Result<Self, Self::Error> {
        Ok(CaptureCallbackState {
            last_frame: std::time::Instant::now(),
        })
    }

    fn on_frame_arrived(
        &mut self,
        frame: &mut windows_capture::frame::Frame,
        capture_control: windows_capture::graphics_capture_api::InternalCaptureControl,
    ) -> Result<(), Self::Error> {
        // Timing debug
        let now = std::time::Instant::now();
        let elapsed = now.duration_since(self.last_frame);
        self.last_frame = now;
        let frame_time = elapsed.as_secs_f64();

        let width = frame.width();
        let height = frame.height();

        eprintln!(
            "dt={:.1}ms, FPS={:.0}, size={width}x{height}",
            frame_time * 1000.,
            1. / frame_time
        );

        Ok(())
    }
}
