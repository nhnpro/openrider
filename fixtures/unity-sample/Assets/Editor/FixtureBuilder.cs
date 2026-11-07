using System;
using System.IO;
using Game;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds this fixture's assets (prefabs, a variant, a nested prefab, a scene with a
/// UnityEvent binding, an animation event) and the C# project files, so they can be
/// regenerated with any Unity version:
/// Unity -batchmode -nographics -projectPath . -executeMethod FixtureBuilder.Build -quit
/// </summary>
public static class FixtureBuilder
{
    public static void Build()
    {
        EditorSettings.serializationMode = SerializationMode.ForceText;
        foreach (var dir in new[] { "Assets/Prefabs", "Assets/Scenes", "Assets/Animations" })
        {
            Directory.CreateDirectory(dir);
        }
        AssetDatabase.Refresh();

        // Walk.anim with a "Footstep" event, played by Player.controller.
        var clip = new AnimationClip { name = "Walk" };
        clip.SetCurve("Model", typeof(Transform), "localPosition.y", AnimationCurve.EaseInOut(0, 0, 0.5f, 0.1f));
        AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { time = 0.25f, functionName = "OnFootstep" } });
        AssetDatabase.CreateAsset(clip, "Assets/Animations/Walk.anim");
        var controller = AnimatorController.CreateAnimatorControllerAtPathWithClip("Assets/Animations/Player.controller", clip);

        // Player.prefab: Player + collider + animator, with a "Model" child.
        var player = new GameObject("Player");
        player.AddComponent<CapsuleCollider>().isTrigger = false;
        player.AddComponent<Animator>().runtimeAnimatorController = controller;
        player.AddComponent<Player>();
        var model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        model.name = "Model";
        UnityEngine.Object.DestroyImmediate(model.GetComponent<Collider>());
        model.transform.SetParent(player.transform, false);
        var playerPrefab = PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab");
        UnityEngine.Object.DestroyImmediate(player);

        // Player Variant.prefab: speed overridden to 5.
        var variantRoot = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
        SetFloat(variantRoot.GetComponent<Player>(), "speed", 5f);
        var variant = PrefabUtility.SaveAsPrefabAsset(variantRoot, "Assets/Prefabs/Player Variant.prefab");
        UnityEngine.Object.DestroyImmediate(variantRoot);

        // Squad.prefab: two nested Player instances, the second with 10 coins.
        var squad = new GameObject("Squad");
        var leader = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, squad.transform);
        leader.name = "Leader";
        var follower = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, squad.transform);
        follower.name = "Follower";
        follower.transform.localPosition = new Vector3(1.5f, 0, 0);
        SetInt(follower.GetComponent<Player>(), "coins", 10);
        PrefabUtility.SaveAsPrefabAsset(squad, "Assets/Prefabs/Squad.prefab");
        UnityEngine.Object.DestroyImmediate(squad);

        // Main.unity: a spawner, a variant instance whose speed is overridden again,
        // a trigger zone bound to it through a UnityEvent, and the squad.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var spawner = new GameObject("Spawner").AddComponent<Spawner>();
        SetObject(spawner, "prefab", playerPrefab.GetComponent<Player>());
        var hero = (GameObject)PrefabUtility.InstantiatePrefab(variant);
        hero.name = "Hero";
        SetFloat(hero.GetComponent<Player>(), "speed", 7f);
        var zone = new GameObject("Trigger Zone");
        zone.AddComponent<BoxCollider>().isTrigger = true;
        var trigger = zone.AddComponent<Trigger>();
        UnityEventTools.AddVoidPersistentListener(trigger.OnEnter, hero.GetComponent<Player>().OnTriggerZoneEntered);
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Squad.prefab"));
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };

        AssetDatabase.SaveAssets();
        GenerateProjectFiles();
        Debug.Log("FixtureBuilder: done");
    }

    static void SetFloat(UnityEngine.Object target, string field, float value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetInt(UnityEngine.Object target, string field, int value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetObject(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>SDK-style .sln/.csproj files, through the Visual Studio Editor package's generator
    /// (the one it uses for VS Code).</summary>
    static void GenerateProjectFiles()
    {
        var type = Type.GetType("Microsoft.Unity.VisualStudio.Editor.SdkStyleProjectGeneration, Unity.VisualStudio.Editor");
        if (type == null)
        {
            Debug.LogError("FixtureBuilder: com.unity.ide.visualstudio is not installed");
            return;
        }
        var generator = Activator.CreateInstance(type, true);
        type.GetMethod("Sync")?.Invoke(generator, null);
    }
}
