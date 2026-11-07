//! Roslyn's non-standard startup against a mock server: the client opens the
//! workspace's solution after `initialized`, counts the server as busy until it
//! reports the projects loaded, and answers restore requests.

use ide_core::event::ServerStatus;
use ide_core::{DocId, IdeEvent, LanguageId};
use ide_jsonrpc::{Handler, Peer};
use ide_language::{LanguageConfig, LanguageRegistry, LanguageServerConfig};
use ide_lsp::{LspManager, ProcessLauncher};
use parking_lot::Mutex;
use serde_json::{Value, json};
use std::sync::Arc;
use std::time::Duration;

#[derive(Default)]
struct ServerLog {
    received: Mutex<Vec<(String, Value)>>,
    peers: Mutex<Vec<Peer>>,
}

struct MockRoslyn(Arc<ServerLog>);

impl Handler for MockRoslyn {
    fn on_notification(&self, method: &str, params: Value) {
        self.0.received.lock().push((method.to_owned(), params));
    }

    fn on_request(&self, method: &str, params: Value) -> Result<Value, (i64, String)> {
        self.0.received.lock().push((method.to_owned(), params));
        match method {
            "initialize" => Ok(json!({ "capabilities": { "textDocumentSync": 1 } })),
            _ => Ok(Value::Null),
        }
    }
}

async fn eventually(mut f: impl FnMut() -> bool) {
    for _ in 0..1000 {
        if f() {
            return;
        }
        tokio::time::sleep(Duration::from_millis(10)).await;
    }
    panic!("condition not reached in time");
}

#[tokio::test(flavor = "multi_thread")]
async fn opens_the_solution_and_is_busy_until_projects_load() {
    let listener = tokio::net::TcpListener::bind(("127.0.0.1", 0)).await.unwrap();
    let port = listener.local_addr().unwrap().port();
    let log = Arc::new(ServerLog::default());
    let _accept = {
        let log = log.clone();
        tokio::spawn(async move {
            while let Ok((stream, _)) = listener.accept().await {
                let (r, w) = stream.into_split();
                let peer = Peer::connect(r, w, Arc::new(MockRoslyn(log.clone())));
                log.peers.lock().push(peer);
            }
        })
    };

    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().join("Game");
    std::fs::create_dir_all(root.join("Assets")).unwrap();
    std::fs::write(root.join("Game.sln"), "").unwrap();
    std::fs::write(root.join("Other.sln"), "").unwrap();
    let server: LanguageServerConfig = toml::from_str(&format!(
        "name = \"roslyn\"\ncommand = \"openidea-no-such-roslyn\"\nconnect = \"127.0.0.1:{port}\""
    ))
    .unwrap();
    let lang: LanguageConfig = toml::from_str("id = \"csharp\"\nname = \"C#\"\nextensions = [\"cs\"]").unwrap();
    let lang = LanguageConfig { language_servers: vec![server], ..lang };
    let registry = Arc::new(LanguageRegistry::from_configs(vec![lang]));
    let events = Arc::new(Mutex::new(Vec::new()));
    let sink = {
        let events = events.clone();
        Arc::new(move |e: IdeEvent| events.lock().push(e))
    };
    let mgr = LspManager::with_launcher(root.clone(), registry, sink, Arc::new(ProcessLauncher));
    let statuses = || -> Vec<ServerStatus> {
        events
            .lock()
            .iter()
            .filter_map(|e| match e {
                IdeEvent::LanguageServer { status, .. } => Some(*status),
                _ => None,
            })
            .collect()
    };

    let path = root.join("Assets/Player.cs");
    std::fs::write(&path, "class Player {}\n").unwrap();
    mgr.did_open(DocId::next(), &path, &LanguageId::new("csharp"), 1, "class Player {}\n");

    let methods = || log.received.lock().iter().map(|(m, _)| m.clone()).collect::<Vec<_>>();
    eventually(|| methods().iter().any(|m| m == "solution/open")).await;
    let all = methods();
    let pos = |name: &str| all.iter().position(|m| m == name).unwrap();
    assert!(pos("initialized") < pos("solution/open"), "{all:?}");
    let solution = log.received.lock().iter().find(|(m, _)| m == "solution/open").unwrap().1.clone();
    assert!(solution["solution"].as_str().unwrap().ends_with("/Game/Game.sln"), "{solution}");

    eventually(|| statuses().contains(&ServerStatus::Busy)).await;
    assert_ne!(statuses().last(), Some(&ServerStatus::Ready));

    let peer = log.peers.lock()[0].clone();
    peer.notify("workspace/projectInitializationComplete", json!({})).unwrap();
    eventually(|| statuses().last() == Some(&ServerStatus::Ready)).await;

    // A restore request is answered at once and the server is asked to restore.
    let projects = json!({ "projectFilePaths": ["/w/Game/Assembly-CSharp.csproj"] });
    let answer =
        peer.request::<_, Value>("workspace/_roslyn_projectNeedsRestore", projects.clone(), None).await.unwrap();
    assert_eq!(answer, Value::Null);
    eventually(|| log.received.lock().iter().any(|(m, p)| m == "workspace/_roslyn_restore" && *p == projects)).await;

    mgr.shutdown_all().await;
}

/// Loading a C# project runs MSBuild targets, analyzers and source generators
/// from the repository: in Restricted Mode the server is not even contacted.
#[tokio::test(flavor = "multi_thread")]
async fn restricted_mode_never_starts_roslyn() {
    let listener = tokio::net::TcpListener::bind(("127.0.0.1", 0)).await.unwrap();
    let port = listener.local_addr().unwrap().port();
    let accepted = Arc::new(Mutex::new(0usize));
    let _accept = {
        let accepted = accepted.clone();
        tokio::spawn(async move {
            while let Ok((_stream, _)) = listener.accept().await {
                *accepted.lock() += 1;
            }
        })
    };
    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().to_path_buf();
    std::fs::write(root.join("Game.sln"), "").unwrap();
    std::fs::write(
        root.join("Directory.Build.targets"),
        "<Project><Target Name=\"Evil\" BeforeTargets=\"Build\" /></Project>",
    )
    .unwrap();
    let server: LanguageServerConfig = toml::from_str(&format!(
        "name = \"roslyn\"\ncommand = \"openidea-no-such-roslyn\"\nconnect = \"127.0.0.1:{port}\""
    ))
    .unwrap();
    let lang: LanguageConfig = toml::from_str("id = \"csharp\"\nname = \"C#\"\nextensions = [\"cs\"]").unwrap();
    let lang = LanguageConfig { language_servers: vec![server], ..lang };
    let registry = Arc::new(LanguageRegistry::from_configs(vec![lang]));
    let events = Arc::new(Mutex::new(Vec::new()));
    let sink = {
        let events = events.clone();
        Arc::new(move |e: IdeEvent| events.lock().push(e))
    };
    let mgr = LspManager::with_launcher(root.clone(), registry, sink, Arc::new(ProcessLauncher));
    mgr.set_execution_allowed(false);
    let path = root.join("Player.cs");
    std::fs::write(&path, "class Player {}\n").unwrap();
    mgr.did_open(DocId::next(), &path, &LanguageId::new("csharp"), 1, "class Player {}\n");
    tokio::time::sleep(Duration::from_secs(1)).await;
    assert_eq!(*accepted.lock(), 0);
    assert!(
        !events.lock().iter().any(|e| matches!(e, IdeEvent::LanguageServer { status: ServerStatus::Ready, .. })),
        "a server started in Restricted Mode"
    );
    mgr.shutdown_all().await;
}
