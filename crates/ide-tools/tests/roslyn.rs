//! `cargo test -p ide-tools --test roslyn -- --ignored --nocapture`
//! Installs the pinned Roslyn language server from nuget.org into a temporary
//! managed-tools directory and starts it.
use ide_tools::{InstallEvent, ToolStatus, Tools};
use std::process::Command;

#[test]
#[ignore = "requires network; downloads the Roslyn language server (about 65 MB) from nuget.org"]
fn real_roslyn_install_from_nuget() {
    if std::env::var_os("OPENIDEA_TEST_OFFLINE").is_some() {
        eprintln!("SKIP real Roslyn: OPENIDEA_TEST_OFFLINE is set");
        return;
    }
    let temp = tempfile::tempdir().unwrap();
    let tools = Tools::new(&temp.path().join("app-data"));
    let lines = parking_lot::Mutex::new(Vec::new());
    let emit = |e: InstallEvent| {
        if let InstallEvent::Line { text } = e {
            eprintln!("{text}");
            lines.lock().push(text);
        }
    };
    let info = tools.install("roslyn", &[], &Default::default(), &emit).unwrap();
    assert_eq!(info.status, ToolStatus::Installed);
    assert_eq!(info.version.as_deref(), Some(ide_tools::registry::ROSLYN_VERSION));
    assert!(lines.lock().iter().any(|l| l == "Checksum verified (SHA-512)"), "{:?}", lines.lock());
    let out = Command::new(AsRef::<std::path::Path>::as_ref(&info.path.unwrap())).arg("--version").output().unwrap();
    assert!(out.status.success(), "{}", String::from_utf8_lossy(&out.stderr));
    assert!(String::from_utf8_lossy(&out.stdout).starts_with("5.13.0"), "{}", String::from_utf8_lossy(&out.stdout));
}
