using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Chapter01.Runtime.Testing;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Chapter01.Editor
{
    // Stage and run the full route before applying the same saved result to Main.
    public static class CastleWalkwayInstaller
    {
        public const string MainScene="Assets/Chapter01/Scenes/Chapter01_Main.unity";
        public const string CandidateScene="Assets/Chapter01/Scenes/Chapter01_CastleWalkwayCandidate.unity";
        public const string Folder="Assets/Chapter01/CastleWalkwayUpgrade";
        public const string Output="../Builds/CastleWalkway-Verification";
        public const string PlayerPath="../Builds/CastleWalkway/ShadowClaw-CastleWalkway.exe";
        const string PlayerAssembly="../Builds/CastleWalkway/ShadowClaw-CastleWalkway_Data/Managed/Assembly-CSharp.dll";
        [Serializable] public class Report
        {
            public string timestampUtc,buildId,sourceSha256,candidateSha256,geometrySha256,scene,combatBefore,combatAfter;
            public int replacedRenderers,replacedGroundColliders,oldLod0Triangles,newTriangles,newRenderers,newGroundColliders,newObstacleColliders;
            public bool sourceSceneUnchanged,authoredTransformsPreserved,combatBindingsPreserved;
            public string[] replacedNames,prefabs;
        }
        [Serializable] class TransformRecord { public string key; public Vector3 position,scale; public Quaternion rotation; }
        [Serializable] class CombatRecord
        {
            public bool authored,exploration;
            public Vector3 spawn,boss,menu,battleCenter,battleSize;
            public Quaternion spawnRotation,bossRotation,menuRotation;
            public string camera,area,moon;
            public string[] lamps,wicks,lights;
        }
        public static T[] All<T>(Scene s) where T:Component=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        public static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
        static string GeometryHash()
        {
            var files=Directory.GetFiles(Folder,"*",SearchOption.AllDirectories).Concat(Directory.GetFiles("Assets/Chapter01/UpperBridgeSample","*",SearchOption.AllDirectories))
                .Concat(Directory.Exists("Assets/Chapter01/WeaponRepair")?Directory.GetFiles("Assets/Chapter01/WeaponRepair","*",SearchOption.AllDirectories):Array.Empty<string>())
                .Concat(new[]{"Assets/ShadowClaw/Resources/HunterWeapon.prefab"})
                .Where(p=>!p.EndsWith(".meta",StringComparison.Ordinal)).OrderBy(p=>p,StringComparer.Ordinal);
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n",files.Select(p=>p.Replace('\\','/')+":"+Hash(p)))))).Replace("-","");
        }
        static string Key(Transform t)=>t.parent?Key(t.parent)+"/"+t.name+"#"+t.GetSiblingIndex():t.name+"#"+t.GetSiblingIndex();
        static TransformRecord[] Transforms(Scene scene)=>All<Transform>(scene).Select(t=>new TransformRecord{key=Key(t),position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray();
        static bool CheckTransforms(Scene scene,TransformRecord[] before)
        {
            var after=All<Transform>(scene).ToDictionary(Key);
            return before.All(x=>after.TryGetValue(x.key,out var t)&&Vector3.Distance(x.position,t.localPosition)<.0001f&&Quaternion.Angle(x.rotation,t.localRotation)<.001f&&Vector3.Distance(x.scale,t.localScale)<.0001f);
        }
        static string Combat(Scene scene)
        {
            var h=All<IronHallEncounter>(scene).Single();
            return JsonUtility.ToJson(new CombatRecord{authored=h.UseAuthoredLayout,exploration=h.StartWithExploration,
                spawn=h.PlayerSpawn.position,boss=h.DemonSpawn.position,menu=h.MenuView.position,spawnRotation=h.PlayerSpawn.rotation,bossRotation=h.DemonSpawn.rotation,menuRotation=h.MenuView.rotation,
                battleCenter=h.BattleArea.bounds.center,battleSize=h.BattleArea.bounds.size,camera=Key(h.GameplayCamera.transform),area=Key(h.BattleArea.transform),moon=Key(h.Moonlight.transform),
                lamps=h.Lanterns.Select(x=>Key(x.transform)).ToArray(),wicks=h.WickAnchors.Select(Key).ToArray(),lights=h.GalleryLights.Select(x=>Key(x.transform)).ToArray()});
        }
        static Material[] Materials()
        {
            var names=new[]{"UB_01_WornSlatePaving","UB_02_WeatheredSideStone","UB_03_WornLimestoneEdges","UB_04_BlackenedWroughtIron","UB_05_AgedBrassFittings"};
            var result=names.Select(n=>AssetDatabase.LoadAssetAtPath<Material>(UpperBridgeSampleMaterials.Folder+"/"+n+".mat")).ToArray();
            if(result.Any(x=>!x))throw new InvalidOperationException("Approved sample materials must exist.");
            return result;
        }
        [MenuItem("Shadow Lab/Castle walkway/Prepare full route")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Output);
            string sourceHash=Hash(MainScene);
            var scene=EditorSceneManager.OpenScene(MainScene);
            if(scene.GetRootGameObjects().Any(g=>g.name=="CastleWalkwayUpgrade"))throw new InvalidOperationException("Main already has the upgrade; use its editable section prefabs instead of rebuilding over layout edits.");
            var transforms=Transforms(scene);string combat=Combat(scene);
            string backup=Path.Combine(Output,"Chapter01_Main.before-walkway.unity");
            if(!File.Exists(backup))File.Copy(MainScene,backup);
            if(!EditorSceneManager.SaveScene(scene,CandidateScene))throw new IOException("Could not create candidate scene");
            var route=Chapter01RuntimeValidation.ReadRoute().points;
            var configuration=CastleWalkwayGeometry.DefaultConfiguration(route);
            var supportSources=new HashSet<string>(CastleBridgeSupportBuilder.GetSourceObjectNames(scene),StringComparer.Ordinal);
            var old=All<MeshRenderer>(scene).Where(r=>CastleWalkwayGeometry.IsReplacedRouteObject(r.name,configuration)||supportSources.Contains(r.name)||r.name.StartsWith("C01__Throne_Ceremonial_Tread_",StringComparison.Ordinal)).ToArray();
            if(old.Length<200)throw new InvalidOperationException("Expected authored road, stair and rail modules.");
            var oldColliders=old.SelectMany(r=>r.GetComponentsInChildren<Collider>(true)).Distinct().ToArray();
            var report=new Report{timestampUtc=DateTime.UtcNow.ToString("o"),buildId=Guid.NewGuid().ToString("N"),sourceSha256=sourceHash,scene=CandidateScene,combatBefore=combat,
                replacedRenderers=old.Length,replacedGroundColliders=oldColliders.Length,oldLod0Triangles=old.Sum(r=>Triangles(r.GetComponent<MeshFilter>()?.sharedMesh)),replacedNames=old.Select(r=>r.name).Distinct().ToArray()};
            foreach(var r in old){r.gameObject.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(r.gameObject);}
            var materials=Materials();
            var root=CastleWalkwayGeometry.Build(route,materials[0],materials[1],materials[2],materials[3],materials[4],configuration);
            SceneManager.MoveGameObjectToScene(root,scene);
            var supportReport=CastleBridgeSupportBuilder.Build(scene,root);
            File.WriteAllText(Path.Combine(Output,"bridge-supports.json"),JsonUtility.ToJson(supportReport,true));
            var actualRoute=CastleWalkwayGeometry.LastExplorationPath;
            var interiorReport=CastleInteriorSurfaceFinisher.Apply(scene,materials[0],materials[1]);
            File.WriteAllText(Path.Combine(Output,"interior-surface.json"),JsonUtility.ToJson(interiorReport,true));
            var obstacleReport=CastleObstacleCollisionBuilder.Build(scene,root.transform,CastleWalkwayGeometry.LastWalkPath);
            File.WriteAllText(Path.Combine(Output,"authored-obstacles.json"),JsonUtility.ToJson(obstacleReport,true));
            var hall=All<IronHallEncounter>(scene).Single();
            hall.GroundColliders=All<Collider>(scene).Where(IsGround).ToArray();EditorUtility.SetDirty(hall);
            Physics.SyncTransforms();
            var descriptor=root.AddComponent<CastleWalkwayDescriptor>();descriptor.root=root.transform;descriptor.buildId=report.buildId;
            descriptor.replacedColliderNames=oldColliders.Select(c=>c.name).Distinct().ToArray();
            descriptor.replacedRendererNames=report.replacedNames;descriptor.scopeNames=report.replacedNames;
            descriptor.allowedGroundColliderNames=hall.GroundColliders.Where(c=>!c.transform.IsChildOf(root.transform)).Select(c=>c.name).Distinct().ToArray();
            var path=new List<Vector3>{hall.PlayerSpawn.position};path.AddRange(actualRoute);
            var bounds=hall.BattleArea.bounds;
            var entrance=new Vector3(actualRoute.Last().x,actualRoute.Last().y,bounds.min.z-.8f);
            entrance=FloorAt(entrance,hall.GroundColliders);
            if(Vector3.Distance(entrance,path.Last())>.1f)path.Add(entrance);
            descriptor.path=path.ToArray();descriptor.entrancePosition=entrance;
            CastleWalkwayProbeBuilder.Configure(scene,descriptor,CastleObstacleCollisionBuilder.LastCreated);
            descriptor.protectedAssetPaths=Directory.GetFiles("Assets/ShadowClaw","*.cs",SearchOption.AllDirectories).OrderBy(x=>x).Select(Path.GetFullPath).ToArray();
            descriptor.protectedAssetSha256=descriptor.protectedAssetPaths.Select(Hash).ToArray();
            report.prefabs=SavePrefabs(root);
            report.authoredTransformsPreserved=CheckTransforms(scene,transforms);report.combatAfter=Combat(scene);report.combatBindingsPreserved=combat==report.combatAfter;
            if(!report.authoredTransformsPreserved||!report.combatBindingsPreserved)throw new InvalidOperationException("Existing layout or combat bindings changed.");
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Cannot save full route candidate.");
            scene=EditorSceneManager.OpenScene(CandidateScene);descriptor=All<CastleWalkwayDescriptor>(scene).Single();root=descriptor.root.gameObject;
            Validate(scene);
            report.newTriangles=root.GetComponentsInChildren<MeshFilter>(true).Sum(x=>Triangles(x.sharedMesh));
            report.newRenderers=root.GetComponentsInChildren<MeshRenderer>(true).Length;
            report.newGroundColliders=root.GetComponentsInChildren<Collider>(true).Count(IsGround);
            report.newObstacleColliders=All<Collider>(scene).Count(c=>c.name.StartsWith("CW_Obstacle_",StringComparison.Ordinal)&&c.gameObject.activeInHierarchy);
            report.sourceSceneUnchanged=Hash(MainScene)==sourceHash;report.candidateSha256=Hash(CandidateScene);report.geometrySha256=GeometryHash();
            File.WriteAllText(Path.Combine(Output,"installation.json"),JsonUtility.ToJson(report,true));
            if(!report.sourceSceneUnchanged)throw new InvalidOperationException("Main scene was edited externally; candidate not published.");
            Debug.Log($"CASTLE_WALKWAY_PREPARE_PASS oldRenderers={report.replacedRenderers} newRenderers={report.newRenderers} oldTriangles={report.oldLod0Triangles} newTriangles={report.newTriangles} obstacles={report.newObstacleColliders}");
        }
        static int Triangles(Mesh m)=>m?Enumerable.Range(0,m.subMeshCount).Sum(i=>(int)m.GetIndexCount(i))/3:0;
        public static bool IsGround(Collider c)=>c&&c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&c.name.StartsWith("Collision__",StringComparison.Ordinal);
        public static Vector3 FloorAt(Vector3 point,IEnumerable<Collider> ground,float up=2.5f,float depth=6f)
        {
            float highest=float.NegativeInfinity;
            foreach(var c in ground)if(IsGround(c)&&c.Raycast(new Ray(point+Vector3.up*up,Vector3.down),out var h,depth)&&h.normal.y>.67f)highest=Mathf.Max(highest,h.point.y);
            if(float.IsNegativeInfinity(highest))throw new InvalidOperationException("No authored ground at "+point);
            point.y=highest+.02f;return point;
        }
        static string[] SavePrefabs(GameObject root)
        {
            string folder=Folder+"/Prefabs";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var results=new List<string>();
            foreach(Transform section in root.transform)
            {
                if(!section.GetComponentsInChildren<MeshRenderer>().Any())continue;
                string path=folder+"/"+section.name+".prefab";
                if(!PrefabUtility.SaveAsPrefabAssetAndConnect(section.gameObject,path,InteractionMode.AutomatedAction))throw new IOException("Could not save "+path);
                results.Add(path);
            }
            return results.ToArray();
        }
        public static void Validate(Scene scene)
        {
            var d=All<CastleWalkwayDescriptor>(scene).Single();var hall=All<IronHallEncounter>(scene).Single();
            if(!hall.UseAuthoredLayout||!hall.StartWithExploration||All<ShadowLab>(scene).Length!=1)throw new InvalidOperationException("Existing player/encounter binding missing.");
            if(All<Collider>(scene).Any(c=>d.replacedColliderNames.Contains(c.name)&&c.enabled&&c.gameObject.activeInHierarchy))throw new InvalidOperationException("Replaced collision is still active.");
            if(All<MeshRenderer>(scene).Any(r=>d.replacedRendererNames.Contains(r.name)&&r.enabled&&r.gameObject.activeInHierarchy))throw new InvalidOperationException("Replaced rendering is still active.");
            var ground=All<Collider>(scene).Where(IsGround).ToArray();Physics.SyncTransforms();
            foreach(var point in d.path) {
                var found=FloorAt(point,ground);
                if(Mathf.Abs(found.y-point.y)>.56f)throw new InvalidOperationException("Path elevation mismatch at "+point+" actual "+found);
            }
            if(Physics.GetIgnoreLayerCollision(2,2))throw new InvalidOperationException("Player layer must collide with safety rail barriers.");
            if(d.root.GetComponentsInChildren<MeshRenderer>(true).Any(r=>r.sharedMaterials.Any(m=>!m||!m.shader)))throw new InvalidOperationException("Missing approved materials.");
            if(hall.ShouldStartEncounter(d.path.Last()))throw new InvalidOperationException("Route test enters battle area.");
            Chapter01CombatInstaller.ValidateScene(scene);
            CastleWeaponRepairValidation.Validate();
        }
        public static void BuildPlayer()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPath));
            var scene=EditorSceneManager.OpenScene(CandidateScene);Validate(scene);
            string settings=Hash("ProjectSettings/EditorBuildSettings.asset");
            var report=BuildPipeline.BuildPlayer(new[]{CandidateScene},PlayerPath,BuildTarget.StandaloneWindows64,BuildOptions.Development);
            if(report.summary.result!=BuildResult.Succeeded||Hash("ProjectSettings/EditorBuildSettings.asset")!=settings)throw new InvalidOperationException("Candidate build failed or changed startup scene.");
            var d=All<CastleWalkwayDescriptor>(scene).Single();
            File.WriteAllText(Path.Combine(Output,"player-build.txt"),"CASTLE_WALKWAY_BUILD_PASS\n"+d.buildId+"\n"+Hash(CandidateScene)+"\n"+GeometryHash()+"\n"+PlayerPath+"\n"+report.summary.totalTime);
            File.WriteAllText(Path.Combine(Output,"player-assembly-sha256.txt"),Hash(PlayerAssembly));
            Debug.Log("CASTLE_WALKWAY_BUILD_PASS");
        }
        public static void PrepareAndBuild(){Prepare();CastleWalkwayCapture.Capture();BuildPlayer();}
        public static void CaptureAndBuild(){CastleWalkwayCapture.Capture();BuildPlayer();}
        [Serializable] class TestResult { public string result,scene,buildId,runtimeAssemblySha256; }
        public static void Publish()
        {
            string marker=Path.Combine(Output,"approved-run.txt");
            if(!File.Exists(marker))throw new InvalidOperationException("Provide the completed real-player acceptance report path.");
            string qaPath=File.ReadAllText(marker).Trim();var qa=JsonUtility.FromJson<TestResult>(File.ReadAllText(qaPath));
            var report=JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Output,"installation.json")));
            if(qa.result!="PASS"||qa.scene!=CandidateScene||qa.buildId!=report.buildId)throw new InvalidOperationException("The current candidate's complete player check must pass before publishing.");
            string combatMarker=Path.Combine(Output,"approved-combat-run.txt");
            if(!File.Exists(combatMarker))throw new InvalidOperationException("The current candidate's combat collision and weapon check is also required.");
            string combatQaPath=File.ReadAllText(combatMarker).Trim();
            var combatQa=JsonUtility.FromJson<TestResult>(File.ReadAllText(combatQaPath));
            if(combatQa.result!="PASS"||combatQa.scene!=CandidateScene||combatQa.buildId!=report.buildId)throw new InvalidOperationException("Combat evidence must pass for this exact candidate build.");
            string assemblyHash=Hash(PlayerAssembly);
            if(!string.Equals(qa.runtimeAssemblySha256,assemblyHash,StringComparison.OrdinalIgnoreCase)||!string.Equals(combatQa.runtimeAssemblySha256,assemblyHash,StringComparison.OrdinalIgnoreCase)||
                File.ReadAllText(Path.Combine(Output,"player-assembly-sha256.txt")).Trim()!=assemblyHash)
                throw new InvalidOperationException("Both real-player checks must come from the exact compiled gameplay and verification assembly being published.");
            if(Hash(MainScene)!=report.sourceSha256)throw new InvalidOperationException("Main was edited externally; preserve that work and regenerate the candidate.");
            if(Hash(CandidateScene)!=report.candidateSha256||!File.ReadAllText(Path.Combine(Output,"player-build.txt")).Contains(report.candidateSha256))throw new InvalidOperationException("Candidate differs from the tested build.");
            if(GeometryHash()!=report.geometrySha256||!File.ReadAllText(Path.Combine(Output,"player-build.txt")).Contains(report.geometrySha256))throw new InvalidOperationException("Generated models or approved materials changed after testing.");
            var scene=EditorSceneManager.OpenScene(CandidateScene);Validate(scene);
            if(Combat(scene)!=report.combatBefore)throw new InvalidOperationException("Combat configuration changed after validation.");
            if(!EditorSceneManager.SaveScene(scene,MainScene))throw new IOException("Cannot publish tested scene to Main.");
            scene=EditorSceneManager.OpenScene(MainScene);Validate(scene);
            if(Combat(scene)!=report.combatBefore)throw new InvalidOperationException("Published encounter configuration mismatch.");
            File.WriteAllText(Path.Combine(Output,"published.txt"),"CASTLE_WALKWAY_PUBLISH_PASS\n"+DateTime.UtcNow.ToString("o")+"\n"+MainScene+"\n"+Hash(MainScene)+"\n"+qaPath+"\n"+combatQaPath);
            Debug.Log("CASTLE_WALKWAY_PUBLISH_PASS");
        }
    }
}
