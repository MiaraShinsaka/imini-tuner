use std::env;

fn main() {
    // The real app must keep requireAdministrator (firmware MSR access).  E2E
    // builds set IMINI_TEST_AS_INVOKER=1 so the test can run in the same
    // non-elevated session as the harness; nothing else changes.
    println!("cargo:rerun-if-env-changed=IMINI_TEST_AS_INVOKER");
    println!("cargo:rerun-if-changed=app.manifest");
    println!("cargo:rerun-if-changed=app.test.manifest");

    let manifest = if env::var_os("IMINI_TEST_AS_INVOKER").is_some() {
        include_str!("app.test.manifest")
    } else {
        include_str!("app.manifest")
    };

    tauri_build::try_build(
        tauri_build::Attributes::new().windows_attributes(
            tauri_build::WindowsAttributes::new().app_manifest(manifest),
        ),
    )
    .expect("failed to run tauri-build");
}
