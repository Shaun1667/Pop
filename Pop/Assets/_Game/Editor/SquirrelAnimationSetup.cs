using System.IO;
using System.Linq;
using System.Text;
using Pop.Player;
using Pop.Rendering;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pop.EditorTools
{
    /// <summary>
    /// Mixamo로 리깅한 플레이어 캐릭터와 애니메이션을 준비한다. 기본 = 날다람쥐(FlyingSquirrel, 2026-11-23부터). 예전 다람쥐도 같은 코드로 다시 넣을 수 있다.
    /// - Pop/Dev/Squirrel/1 Setup Mixamo Assets: 텍스처 · 머티리얼 + FBX 임포트 설정(Humanoid, 클립 범위 · 루프) + Animator Controller를 만든다.
    /// - Pop/Dev/Squirrel/2 Replace Player Visual (open scene): 열린 씬의 Player/Visual 아래 캐릭터만 새 모델로 바꾼다(이전 다람쥐 · 날다람쥐는 지움, 멱등).
    ///   Player 계층 밖의 오브젝트는 건드리지 않는다.
    /// - Pop/Dev/Squirrel/Old Squirrel/...: 같은 일을 예전 다람쥐(Squirrel_Explorer_Mixamo)로 한다.
    /// 활공 기능은 없다(겉모습만 바꿈, 사용자 결정 2026-11-23).
    /// 날다람쥐는 달릴 때 비막(팔)을 펴고 가끔 퍼덕인다(보이는 것만): 컨트롤러의 Wings 레이어(온몸을 덮는 활공 자세: 다리 · 몸 · 머리는 가만히, 팔만 퍼덕임. 코드로 만든 WingsFlap 클립).
    /// 레이어 무게는 PlayerAnimationVisual이 속도로 정한다. 이동 · 높이 · 캡슐은 그대로다.
    /// </summary>
    public static class SquirrelAnimationSetup
    {
        public class Character
        {
            public string label;
            public string dir;              // Mixamo FBX 폴더
            public string modelFile;        // 스킨 모델
            public string filePrefix;       // <prefix>@HappyIdle.fbx ...
            public string clipPrefix;       // <prefix>_Idle ...
            public string controllerFile;
            public string materialPath;
            public string fbxMaterialName;  // FBX 안의 머티리얼 슬롯 이름
            public string textureDir;       // null이면 머티리얼을 있는 그대로 쓴다. 있으면 base_color / normal / orm에서 머티리얼을 만든다
            public string instanceName;     // Player/Visual 아래 이름
            public float runTimeScale;      // Run Forward 재생 배속
            public bool wings;              // 달릴 때 팔(비막)을 펴고 퍼덕이는 Wings 레이어를 넣는다
            public string toonSettingsPath; // 있으면 머티리얼을 Pop/Toon Character로 바꾸고 이 설정 값을 넣는다 (null = URP Lit 그대로)

            public string WingsClipPath => dir + "/" + clipPrefix + "_WingsFlap.anim";
            public string WingsMaskPath => dir + "/" + clipPrefix + "_Wings.mask";

            public string ModelPath => dir + "/" + modelFile;
            public string ControllerPath => dir + "/" + controllerFile;
        }

        /// <summary>날다람쥐 (기본)</summary>
        public static readonly Character Flying = new Character
        {
            label = "FlyingSquirrel",
            dir = "Assets/_Game/Art/Characters/FlyingSquirrel/Mixamo",
            modelFile = "FlyingSquirrel_Explorer_Mixamo.fbx",
            filePrefix = "FlyingSquirrel",
            clipPrefix = "FlyingSquirrel",
            controllerFile = "FlyingSquirrel.controller",
            materialPath = "Assets/_Game/Art/Characters/FlyingSquirrel/Materials/M_FlyingSquirrel.mat",
            fbxMaterialName = "M_FlyingSquirrel",
            textureDir = "Assets/_Game/Art/Characters/FlyingSquirrel/Textures",
            instanceName = "FlyingSquirrel",
            runTimeScale = 3.0f,
            wings = true,
            toonSettingsPath = "Assets/_Game/Art/Shaders/ToonSettings_FlyingSquirrel.asset",
        };

        /// <summary>예전 다람쥐 (에셋은 그대로 남겨 둠)</summary>
        public static readonly Character Classic = new Character
        {
            label = "Squirrel",
            dir = "Assets/_Game/Art/Characters/Squirrel/Mixamo",
            modelFile = "Squirrel_Explorer_Mixamo.fbx",
            filePrefix = "Squirrel",
            clipPrefix = "Squirrel",
            controllerFile = "Squirrel.controller",
            materialPath = "Assets/_Game/Art/Characters/Squirrel/Materials/M_Squirrel_Explorer_Rig.mat",
            fbxMaterialName = "M_Squirrel",
            textureDir = null,
            instanceName = "Squirrel",
            runTimeScale = 3.0f,
        };

        public static readonly Character Default = Flying;

        // 맨 처음의 VARCO 리그 FBX(지금은 없음). 씬에 남아 있으면 지운다.
        const string LegacyRigModelPath = "Assets/_Game/Art/Characters/Squirrel/Squirrel_Explorer_Rig.fbx";

        public const float PlayerHeight = 1.2f;       // 캡슐 키 (m)
        public const float WalkSpeed = 0.9f;          // Happy Walk가 1배속으로 덮는 속도 (m/s, 키 1.2 m 기준)
        public const float RunSpeed = 6f;             // PlayerTuning.moveSpeed
        public const float FallVSpeed = -12f;         // 이보다 빨리 떨어지면 Fall 자세

        struct ClipDef
        {
            public string file, clip; public bool loop, bakeY; public int first, last;
            public ClipDef(string f, string c, bool l, bool by, int a = -1, int b = -1) { file = f; clip = c; loop = l; bakeY = by; first = a; last = b; }
        }

        // Jump = Mixamo "Jump"(Jump In Place, 79프레임). 웅크리는 준비(0~23)와 착지 뒤(43~)는 버리고 공중 구간(도약 24 ~ 착지 42)만 쓴다.
        // file = <filePrefix>@<file>.fbx, clip = <clipPrefix>_<clip>
        static readonly ClipDef[] Clips =
        {
            new ClipDef("HappyIdle", "Idle", true, true),
            new ClipDef("HappyWalk", "Walk", true, true),
            new ClipDef("RunForward", "Run", true, true),
            new ClipDef("Jump", "JumpAir", false, false, 24, 42),
            new ClipDef("FallingIdle", "Fall", true, true),
        };

        static string ClipFile(Character ch, int i) => ch.dir + "/" + ch.filePrefix + "@" + Clips[i].file + ".fbx";

        [MenuItem("Pop/Dev/Squirrel/1 Setup Mixamo Assets")]
        public static void SetupAssetsMenu() => Debug.Log(SetupAssets(Default));

        [MenuItem("Pop/Dev/Squirrel/2 Replace Player Visual (open scene)")]
        public static void ReplaceMenu() => Debug.Log(ReplaceInActiveScene(Default));

        [MenuItem("Pop/Dev/Squirrel/Old Squirrel/1 Setup Mixamo Assets")]
        public static void SetupOldAssetsMenu() => Debug.Log(SetupAssets(Classic));

        [MenuItem("Pop/Dev/Squirrel/Old Squirrel/2 Replace Player Visual (open scene)")]
        public static void ReplaceOldMenu() => Debug.Log(ReplaceInActiveScene(Classic));

        [MenuItem("Pop/Dev/Squirrel/Apply Toon Material")]
        public static void ApplyToonMenu()
        {
            var log = new StringBuilder();
            ApplyToon(Default, AssetDatabase.LoadAssetAtPath<Material>(Default.materialPath), log);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        /// <summary>toonSettingsPath의 설정(없으면 기본값으로 만듦)대로 머티리얼 셰이더를 Pop/Toon Character(또는 useToon = false면 URP Lit)로 두고 값을 넣는다.
        /// 같은 머티리얼 파일의 셰이더만 바꾸므로 GUID · FBX remap · 씬 참조는 그대로. _BaseMap · _BaseColor · _BumpMap 이름이 같아 텍스처도 그대로 간다.</summary>
        public static void ApplyToon(Character ch, Material mat, StringBuilder log)
        {
            if (mat == null || string.IsNullOrEmpty(ch.toonSettingsPath)) return;
            var s = AssetDatabase.LoadAssetAtPath<ToonCharacterSettings>(ch.toonSettingsPath);
            if (s == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ch.toonSettingsPath));
                s = ScriptableObject.CreateInstance<ToonCharacterSettings>();
                AssetDatabase.CreateAsset(s, ch.toonSettingsPath);
                log.AppendLine("created " + ch.toonSettingsPath);
            }
            var shader = Shader.Find(s.useToon ? ToonCharacterSettings.ShaderName : "Universal Render Pipeline/Lit");
            if (shader == null) { log.AppendLine("셰이더가 없다: " + (s.useToon ? ToonCharacterSettings.ShaderName : "URP Lit")); return; }
            if (mat.shader != shader) mat.shader = shader;
            if (s.useToon) s.ApplyTo(mat);
            else mat.SetFloat("_BumpScale", 1f);
            EditorUtility.SetDirty(mat);
            log.AppendLine($"material {ch.materialPath}: shader {mat.shader.name} (settings {ch.toonSettingsPath})");
        }

        public static string SetupAssets() => SetupAssets(Default);
        public static string ReplaceInActiveScene() => ReplaceInActiveScene(Default);

        public static string SetupAssets(Character ch)
        {
            var log = new StringBuilder(ch.label + " setup\n");
            AssetDatabase.Refresh();

            // 0) 텍스처 · 머티리얼
            var mat = EnsureMaterial(ch, log);
            ApplyToon(ch, mat, log);

            // 1) 스킨 모델
            var mi = AssetImporter.GetAtPath(ch.ModelPath) as ModelImporter;
            if (mi == null) return "모델이 없다: " + ch.ModelPath;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            if (mat != null)
            {
                foreach (var kv in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
                    mi.RemoveRemap(kv);
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), ch.fbxMaterialName), mat);
            }
            mi.SaveAndReimport();
            log.AppendLine(DescribeAvatar(ch.ModelPath));

            // 2) 애니메이션 FBX (스킨 없음). 뼈대만 있어도 Humanoid 아바타를 만들 수 있다.
            //    Humanoid라서 파일 단위(cm, 0.01)와 모델 단위(m)가 달라도 엉덩이 이동은 아바타 크기에 맞춰진다.
            for (int i = 0; i < Clips.Length; i++)
            {
                var c = Clips[i];
                string path = ClipFile(ch, i);
                var ai = AssetImporter.GetAtPath(path) as ModelImporter;
                if (ai == null) { log.AppendLine("없음: " + path); continue; }
                ai.animationType = ModelImporterAnimationType.Human;
                ai.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                ai.importAnimation = true;
                ai.materialImportMode = ModelImporterMaterialImportMode.None;
                ai.importCameras = false;
                ai.importLights = false;
                ai.SaveAndReimport();

                ai = (ModelImporter)AssetImporter.GetAtPath(path);
                var defs = ai.defaultClipAnimations;
                if (defs == null || defs.Length == 0) { log.AppendLine("클립 없음: " + path); continue; }
                var d = defs[0];
                d.name = ch.clipPrefix + "_" + c.clip;
                if (c.first >= 0) { d.firstFrame = c.first; d.lastFrame = c.last; }
                d.loopTime = c.loop;
                d.loopPose = c.loop;
                d.lockRootRotation = true;        // 회전은 자세에 굽는다 (Root Motion 안 씀)
                d.keepOriginalOrientation = true;
                d.lockRootPositionXZ = true;
                d.keepOriginalPositionXZ = true;
                d.lockRootHeightY = c.bakeY;      // 점프만 높이를 굽지 않는다: 높이는 물리(캡슐)가 정하고 발이 캡슐 바닥에 맞는다
                d.keepOriginalPositionY = c.bakeY;
                d.heightFromFeet = !c.bakeY;
                ai.clipAnimations = new[] { d };
                ai.SaveAndReimport();
                var clip = LoadClip(path);
                log.AppendLine(DescribeAvatar(path) + (clip != null
                    ? $" clip={clip.name} len={clip.length:F3}s fps={clip.frameRate} loop={clip.isLooping} humanMotion={clip.humanMotion}"
                    : " clip=NULL"));
            }

            log.AppendLine(BuildController(ch));
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        /// <summary>textureDir이 있으면 base_color / normal / orm에서 URP Lit 머티리얼을 만든다(있으면 고쳐 쓴다 → GUID 유지).
        /// metallic_smoothness.png = orm에서 만든다: R = ORM.B(금속), A = 1 - ORM.G(거칠기 → 매끄러움).</summary>
        static Material EnsureMaterial(Character ch, StringBuilder log)
        {
            if (string.IsNullOrEmpty(ch.textureDir))
                return AssetDatabase.LoadAssetAtPath<Material>(ch.materialPath);

            string dir = ch.textureDir + "/";
            if (!File.Exists(dir + "base_color.png") || !File.Exists(dir + "normal.png") || !File.Exists(dir + "orm.png"))
            {
                log.AppendLine("텍스처가 빠졌다: " + dir + " (base_color / normal / orm)");
                return AssetDatabase.LoadAssetAtPath<Material>(ch.materialPath);
            }
            SetTex(dir + "base_color.png", TextureImporterType.Default, true);
            SetTex(dir + "normal.png", TextureImporterType.NormalMap, false);
            SetTex(dir + "orm.png", TextureImporterType.Default, false);
            string ms = dir + "metallic_smoothness.png";
            if (!File.Exists(ms) || File.GetLastWriteTimeUtc(ms) < File.GetLastWriteTimeUtc(dir + "orm.png"))
            {
                MakeMetallicSmoothness(dir + "orm.png", ms);
                AssetDatabase.ImportAsset(ms);
                log.AppendLine("made " + ms);
            }
            SetTex(ms, TextureImporterType.Default, false);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(ch.materialPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) { log.AppendLine("URP Lit 셰이더가 없다"); return null; }
                Directory.CreateDirectory(Path.GetDirectoryName(ch.materialPath));
                AssetDatabase.Refresh();
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, ch.materialPath);
                log.AppendLine("created " + ch.materialPath);
            }
            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "base_color.png");
            mat.SetTexture("_BaseMap", baseTex);
            mat.SetTexture("_MainTex", baseTex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "normal.png"));
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ms));
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.6f);   // 예전 다람쥐(M_Squirrel_Explorer_Rig)와 같음(맵 알파에 곱해짐)
            EditorUtility.SetDirty(mat);
            log.AppendLine($"material {ch.materialPath}: base {(baseTex != null ? "ok" : "MISSING")}, normal {(mat.GetTexture("_BumpMap") != null ? "ok" : "MISSING")}, metallic_smoothness {(mat.GetTexture("_MetallicGlossMap") != null ? "ok" : "MISSING")}");
            return mat;
        }

        static void SetTex(string path, TextureImporterType type, bool srgb)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { AssetDatabase.ImportAsset(path); ti = AssetImporter.GetAtPath(path) as TextureImporter; }
            if (ti == null) return;
            if (ti.textureType == type && ti.sRGBTexture == srgb && ti.maxTextureSize == 2048) return;
            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
        }

        static void MakeMetallicSmoothness(string ormPath, string outPath)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            src.LoadImage(File.ReadAllBytes(ormPath));
            var px = src.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                px[i] = new Color32(c.b, 0, 0, (byte)(255 - c.g));
            }
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
            dst.SetPixels32(px);
            dst.Apply();
            File.WriteAllBytes(outPath, dst.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(dst);
        }

        static string DescribeAvatar(string path)
        {
            var av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            return $"{Path.GetFileName(path)}: avatar={(av == null ? "none" : (av.isValid ? "valid" : "INVALID") + (av.isHuman ? "/human" : "/NOT human"))}";
        }

        static AnimationClip LoadClip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        public static string BuildController() => BuildController(Default);

        /// <summary>컨트롤러를 새로 만든다. 이미 있으면 파일 내용만 바꿔 GUID를 지킨다(씬의 Animator 참조가 안 끊긴다).</summary>
        public static string BuildController(Character chr)
        {
            var idle = LoadClip(ClipFile(chr, 0));
            var walk = LoadClip(ClipFile(chr, 1));
            var run = LoadClip(ClipFile(chr, 2));
            var jump = LoadClip(ClipFile(chr, 3));
            var fall = LoadClip(ClipFile(chr, 4));
            if (idle == null || walk == null || run == null || jump == null || fall == null)
                return "컨트롤러를 못 만들었다: 클립이 빠졌다";

            const string tmpName = "__SquirrelControllerBuild";
            string tmpPath = chr.dir + "/" + tmpName + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(tmpPath) != null) AssetDatabase.DeleteAsset(tmpPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(tmpPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter(new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            ctrl.AddParameter(new AnimatorControllerParameter { name = "JumpT", type = AnimatorControllerParameterType.Float, defaultFloat = 0.5f });
            ctrl.AddParameter("VSpeed", AnimatorControllerParameterType.Float);

            var sm = ctrl.layers[0].stateMachine;

            // 대기 · 걷기 · 달리기: 바닥 속도로 섞는다
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, WalkSpeed);
            tree.AddChild(run, RunSpeed);
            var ch = tree.children;
            ch[2].timeScale = chr.runTimeScale;
            tree.children = ch;
            EditorUtility.SetDirty(tree);

            // 공중: 점프 공중 구간을 JumpT(위쪽 속도)로 직접 넘긴다 → 체공 시간에 자동으로 맞는다
            var air = sm.AddState("Air");
            air.motion = jump;
            air.timeParameterActive = true;
            air.timeParameter = "JumpT";

            var fallState = sm.AddState("Fall");
            fallState.motion = fall;

            sm.defaultState = loco;

            // 달릴 때 활공 자세(온몸 레이어): 다리 · 몸 · 머리는 가만히, 비막을 펴고 가끔 퍼덕인다. 달리기 다리는 완전히 덮인다. 무게는 PlayerAnimationVisual이 속도로 넣는다(기본 0 = 안 보임)
            string wingsInfo = "";
            if (chr.wings)
            {
                var mask = EnsureWingsMask(chr);
                var flap = EnsureWingsClip(chr);
                ctrl.AddLayer(PlayerAnimationVisual.WingsLayerName);
                var layers = ctrl.layers;
                var wl = layers[layers.Length - 1];
                wl.avatarMask = mask;
                wl.blendingMode = AnimatorLayerBlendingMode.Override;
                wl.defaultWeight = 0f;
                var ws = wl.stateMachine.AddState("WingsFlap");
                ws.motion = flap;
                wl.stateMachine.defaultState = ws;
                ctrl.layers = layers;
                wingsInfo = $", layer {PlayerAnimationVisual.WingsLayerName}[WingsFlap {flap.length:F2}s, full body glide pose]";
            }

            Link(loco, air, 0.06f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            var land = Link(air, loco, 0.10f);
            land.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            land.interruptionSource = TransitionInterruptionSource.Destination; // 착지 직후 다시 점프하면 바로 공중으로
            Link(air, fallState, 0.25f).AddCondition(AnimatorConditionMode.Less, FallVSpeed, "VSpeed");
            var land2 = Link(fallState, loco, 0.12f);
            land2.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            land2.interruptionSource = TransitionInterruptionSource.Destination;
            Link(fallState, air, 0.2f).AddCondition(AnimatorConditionMode.Greater, FallVSpeed * 0.5f, "VSpeed");

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();

            string finalPath = chr.ControllerPath;
            string how;
            if (File.Exists(finalPath))
            {
                // 내용만 바꾼다 (.meta = GUID 그대로)
                string text = File.ReadAllText(tmpPath).Replace("m_Name: " + tmpName, "m_Name: " + Path.GetFileNameWithoutExtension(finalPath));
                File.WriteAllText(finalPath, text);
                AssetDatabase.DeleteAsset(tmpPath);
                AssetDatabase.ImportAsset(finalPath, ImportAssetOptions.ForceUpdate);
                how = "updated in place";
            }
            else
            {
                string err = AssetDatabase.MoveAsset(tmpPath, finalPath);
                how = string.IsNullOrEmpty(err) ? "created" : "MOVE FAILED: " + err;
            }
            return $"controller: {finalPath} {how} (Locomotion[Idle 0 / Walk {WalkSpeed} / Run {RunSpeed} x{chr.runTimeScale}], Air[JumpT], Fall{wingsInfo})";
        }

        // ── 비막(날개) 펴기 · 퍼덕이기 클립 ─────────────────────────────────────────
        // Humanoid 근육 값으로 만든다(뼈 이름에 안 매임). 온몸 자세: Idle 클립 첫 프레임을 고정값으로 깔고(엉덩이 위치 · 방향 포함 → 흔들림 없음) 몸통 · 다리 · 팔을 덮어쓴다.
        public const float WingsLoop = 2.4f;        // 한 바퀴 (s). 앞 1.5초는 펴고 미끄러지듯, 뒤에 빠르게 두 번 퍼덕인다
        public const float WingLevel = 0.40f;       // Arm Down-Up: 팔이 수평일 때의 근육 값 (T자세 ≈ 0.4)
        public const float WingDegToMuscle = 1f / 100f; // Arm Down-Up 1도 = 근육 값 0.01 (위쪽 범위 100도)
        public const float WingFront = 0.42f;       // Arm Front-Back: 이 아바타에서 0.30 = 정확히 옆, 클수록 뒤로. 0.42 = 조금 뒤로 젖힘(옆 시점에서 몸 뒤로 비막이 보인다)
        public const float WingTwist = 0.05f;       // Arm Twist In-Out: 손바닥이 아래 (T자세 ≈ 0.05)
        public const float WingStretch = 0.95f;     // Forearm Stretch: 거의 다 편 팔
        public const float WingBodyCompUp = 0.00081f;   // 팔을 1도 올릴 때 RootT.y에 더하는 값(엉덩이가 내려가는 것을 상쇄)
        public const float WingBodyCompDown = 0.00068f; // 팔을 1도 내릴 때

        // (시간 s, 수평에서 위로 몇 도). 처음과 끝이 같아야 루프가 이어진다
        static readonly float[,] WingElevation =
        {
            { 0.00f,   0f }, { 0.55f,   4f }, { 1.10f,  -3f }, { 1.52f,   2f },
            { 1.66f,  28f }, { 1.79f, -36f }, { 1.92f,  24f }, { 2.05f, -30f }, { 2.22f,   6f },
            { WingsLoop, 0f },
        };

        // 활공 자세의 몸통 · 목 · 머리 근육 값 (0 = 중립, 곧게). 앞으로 기울임과 떠 있는 높이는 PlayerAnimationVisual이 준다
        static readonly System.Collections.Generic.Dictionary<string, float> GlideBody = new System.Collections.Generic.Dictionary<string, float>
        {
            { "Spine Front-Back", 0f }, { "Spine Left-Right", 0f }, { "Spine Twist Left-Right", 0f },
            { "Chest Front-Back", 0f }, { "Chest Left-Right", 0f }, { "Chest Twist Left-Right", 0f },
            { "UpperChest Front-Back", 0f }, { "UpperChest Left-Right", 0f }, { "UpperChest Twist Left-Right", 0f },
            { "Neck Nod Down-Up", 0f }, { "Neck Tilt Left-Right", 0f }, { "Neck Turn Left-Right", 0f },
            { "Head Nod Down-Up", 0f }, { "Head Tilt Left-Right", 0f }, { "Head Turn Left-Right", 0f },
        };

        // 활공 자세의 다리 근육 값 (좌우 같음): 뒤로 · 조금 옆으로 뻗고 무릎은 거의 편다. 움직이지 않는다
        static readonly System.Collections.Generic.Dictionary<string, float> GlideLeg = new System.Collections.Generic.Dictionary<string, float>
        {
            { "Upper Leg Front-Back", 1.0f },    // 0.6 근처 = 서 있음, 1.0 = 뒤로 끝까지 (이 아바타에서 약 30도. 눈으로 확인함)
            { "Upper Leg In-Out", 0.5f },        // + = 옆으로 벌림 (- 는 무릎이 안으로 모인다. 확인함)
            { "Upper Leg Twist In-Out", 0f },
            { "Lower Leg Stretch", 0.9f },       // 1 = 곧게
            { "Lower Leg Twist In-Out", 0f },
            { "Foot Up-Down", -0.6f },           // - = 발끝을 뒤 · 아래로 편다 (확인함)
            { "Foot Twist In-Out", 0f },
            { "Toes Up-Down", 0f },
        };

        static AnimationCurve WingCurve(System.Func<float, float> fromDeg)
        {
            int n = WingElevation.GetLength(0);
            var keys = new Keyframe[n];
            for (int i = 0; i < n; i++) keys[i] = new Keyframe(WingElevation[i, 0], fromDeg(WingElevation[i, 1]));
            var c = new AnimationCurve(keys);
            for (int i = 0; i < n; i++) c.SmoothTangents(i, 0f);
            // 루프 이음매: 처음 · 끝 기울기를 0으로 맞춘다
            var k0 = c[0]; k0.inTangent = k0.outTangent = 0f; c.MoveKey(0, k0);
            var k1 = c[n - 1]; k1.inTangent = k1.outTangent = 0f; c.MoveKey(n - 1, k1);
            return c;
        }

        static AnimationCurve Flat(float v) => new AnimationCurve(new Keyframe(0f, v), new Keyframe(WingsLoop, v));

        /// <summary>팔(비막)을 옆으로 펴고 가끔 두 번 퍼덕이는 루프 클립. 있으면 내용만 바꾼다(GUID 유지).</summary>
        public static AnimationClip EnsureWingsClip(Character chr)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(chr.WingsClipPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, chr.WingsClipPath);
            }
            clip.ClearCurves();
            clip.frameRate = 30f;

            // 1) 바탕: Idle 첫 프레임의 모든 Humanoid 커브(RootT · RootQ · 근육 · 발 IK)를 고정값으로 → 서 있는 높이 그대로, 움직임 없음
            var idle = LoadClip(ClipFile(chr, 0));
            if (idle != null)
            {
                foreach (var b in AnimationUtility.GetCurveBindings(idle))
                {
                    if (b.type != typeof(Animator) || !string.IsNullOrEmpty(b.path)) continue;
                    var src = AnimationUtility.GetEditorCurve(idle, b);
                    if (src == null || src.length == 0) continue;
                    AnimationUtility.SetEditorCurve(clip, b, Flat(src.Evaluate(0f)));
                }
            }

            // 2) 몸통 · 목 · 머리는 곧게(0 = 중립), 다리는 뒤 · 옆으로 뻗은 활공 자세로 고정
            foreach (var kv in GlideBody) clip.SetCurve("", typeof(Animator), kv.Key, Flat(kv.Value));
            // 몸 방향은 똑바로(Idle 첫 프레임의 작은 기울임 · 옆 치우침을 없앤다 → 좌우 대칭)
            clip.SetCurve("", typeof(Animator), "RootQ.x", Flat(0f));
            clip.SetCurve("", typeof(Animator), "RootQ.y", Flat(0f));
            clip.SetCurve("", typeof(Animator), "RootQ.z", Flat(0f));
            clip.SetCurve("", typeof(Animator), "RootQ.w", Flat(1f));
            clip.SetCurve("", typeof(Animator), "RootT.x", Flat(0f));
            foreach (string side in new[] { "Left", "Right" })
                foreach (var kv in GlideLeg) clip.SetCurve("", typeof(Animator), side + " " + kv.Key, Flat(kv.Value));

            // 3) 팔(비막): 펴고 가끔 퍼덕임
            foreach (string side in new[] { "Left", "Right" })
            {
                void Set(string muscle, AnimationCurve c) => clip.SetCurve("", typeof(Animator), side + " " + muscle, c);
                Set("Shoulder Down-Up", WingCurve(d => d * 0.004f));              // 어깨도 조금 따라간다
                Set("Shoulder Front-Back", Flat(0f));
                Set("Arm Down-Up", WingCurve(d => WingLevel + d * WingDegToMuscle));
                Set("Arm Front-Back", Flat(WingFront));
                Set("Arm Twist In-Out", Flat(WingTwist));
                Set("Forearm Stretch", WingCurve(d => WingStretch - Mathf.Max(0f, -d) * 0.006f)); // 내리칠 때 팔꿈치를 살짝 굽힌다
                Set("Forearm Twist In-Out", Flat(0f));
                Set("Hand Down-Up", WingCurve(d => -d * 0.008f));                 // 손끝은 반대로 늦게 따라온다
                Set("Hand In-Out", Flat(0f));
            }

            // 4) 퍼덕일 때 몸 · 머리가 따라 움직이지 않게: Humanoid의 RootT는 무게중심이라 팔을 올리면 엉덩이가 그만큼 내려간다.
            //    팔 각도만큼 RootT.y를 되돌려 엉덩이 높이를 고정한다 (이 아바타에서 잰 값)
            var rootY = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y");
            var rootYCurve = AnimationUtility.GetEditorCurve(clip, rootY);
            if (rootYCurve != null && rootYCurve.length > 0)
            {
                float baseY = rootYCurve.Evaluate(0f);
                AnimationUtility.SetEditorCurve(clip, rootY, WingCurve(d => baseY + d * (d > 0f ? WingBodyCompUp : WingBodyCompDown)));
            }

            
// 루트 굽기 설정은 Idle과 같게(같은 RootT · RootQ가 같은 자세가 되도록)
            var st = idle != null ? AnimationUtility.GetAnimationClipSettings(idle) : AnimationUtility.GetAnimationClipSettings(clip);
            st.additiveReferencePoseClip = null;
            st.hasAdditiveReferencePose = false;
            st.cycleOffset = 0f;
            st.mirror = false;
            st.loopTime = true;
            st.startTime = 0f;
            st.stopTime = WingsLoop;
            AnimationUtility.SetAnimationClipSettings(clip, st);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>온몸을 켠 Humanoid 마스크(달리기 다리 · 엉덩이 흔들림이 새지 않게). 있으면 고쳐 쓴다(GUID 유지).</summary>
        public static AvatarMask EnsureWingsMask(Character chr)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(chr.WingsMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, chr.WingsMaskPath);
            }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
            }
            EditorUtility.SetDirty(mask);
            return mask;
        }

        static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = duration;
            t.canTransitionToSelf = false;
            return t;
        }

        static Bounds MeshWorldBounds(SkinnedMeshRenderer smr)
        {
            Bounds mb = smr.sharedMesh.bounds;
            Matrix4x4 m = smr.transform.localToWorldMatrix;
            var b = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                b.Encapsulate(m.MultiplyPoint3x4(c));
            }
            return b;
        }

        /// <summary>열린 씬의 Player/Visual 아래 캐릭터(예전 다람쥐 · 날다람쥐 어느 쪽이든)를 지우고 ch 모델로 바꾼다. Player 계층만 건드린다.
        /// save = false면 씬을 저장하지 않고 dirty로만 둔다.</summary>
        public static string ReplaceInActiveScene(Character ch, bool save = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Play 중에는 하지 않는다";
            var scene = EditorSceneManager.GetActiveScene();
            var player = Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (player == null) return scene.name + ": PlayerController가 없다";
            var visual = player.transform.Find("Visual");
            if (visual == null) return scene.name + ": Player/Visual이 없다";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ch.ModelPath);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ch.ControllerPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ch.materialPath);
            if (model == null || ctrl == null) return "모델이나 컨트롤러가 없다 (먼저 1 Setup Mixamo Assets)";

            var log = new StringBuilder(scene.name + " <- " + ch.label + ": ");

            // 지금 있는 캐릭터(예전 다람쥐, 날다람쥐, 다시 실행했을 때의 같은 모델)만 지운다
            string[] known = { Flying.ModelPath, Classic.ModelPath, LegacyRigModelPath };
            for (int i = visual.childCount - 1; i >= 0; i--)
            {
                var child = visual.GetChild(i).gameObject;
                string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child);
                if (!string.IsNullOrEmpty(src) && System.Array.IndexOf(known, src) >= 0)
                {
                    log.Append($"removed '{child.name}' ({Path.GetFileName(src)}); ");
                    Undo.DestroyObjectImmediate(child);
                }
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, visual);
            Undo.RegisterCreatedObjectUndo(inst, "Replace player visual");
            inst.name = ch.instanceName;
            var tr = inst.transform;
            tr.localPosition = Vector3.zero;
            tr.localRotation = Quaternion.identity;
            tr.localScale = Vector3.one;

            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null) return log + "SkinnedMeshRenderer가 없다";
            if (mat != null && smr.sharedMaterial != mat) { smr.sharedMaterial = mat; log.Append("material override; "); }
            smr.updateWhenOffscreen = true;

            // 키 1.2 m, 발이 캡슐 바닥(Visual 원점), 엉덩이가 캡슐 축 위
            // 렌더러 bounds는 여유가 붙어 있어서, 메시 자체(바인드 자세)의 범위로 잰다
            Bounds b = MeshWorldBounds(smr);
            float k = PlayerHeight / Mathf.Max(0.0001f, b.size.y);
            tr.localScale = Vector3.one * k;
            b = MeshWorldBounds(smr);
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            Transform hips = anim.avatar != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips == null) hips = inst.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.EndsWith("Hips"));
            Vector3 shift = Vector3.zero;
            shift.y = visual.position.y - b.min.y;
            if (hips != null)
            {
                shift.x = visual.position.x - hips.position.x;
                shift.z = visual.position.z - hips.position.z;
            }
            tr.position += shift;

            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.updateMode = AnimatorUpdateMode.Normal;

            var pav = player.GetComponent<PlayerAnimationVisual>();
            if (pav == null) pav = Undo.AddComponent<PlayerAnimationVisual>(player.gameObject);
            Undo.RecordObject(pav, "Replace player visual");
            pav.animator = anim;
            EditorUtility.SetDirty(pav);

            var facing = player.GetComponent<PlayerFacingVisual>();
            b = MeshWorldBounds(smr);
            log.Append($"scale local {tr.localScale.x:F4} (mesh world height {b.size.y:F3} m), feet y {b.min.y:F3} vs visual y {visual.position.y:F3}, " +
                       $"local pos {tr.localPosition.ToString("F4")}, avatar {(anim.avatar != null && anim.avatar.isValid && anim.isHuman ? "humanoid ok" : "PROBLEM")}, " +
                       $"material {(smr.sharedMaterial != null ? smr.sharedMaterial.name : "NONE")}, controller {ctrl.name}, " +
                       $"facing.visual {(facing != null && facing.visual == visual ? "ok" : "CHECK")}, stunVisual {(player.GetComponent<PlayerStunVisual>() != null ? "ok" : "none")}, " +
                       $"animVisual.animator {(pav.animator == anim ? "ok" : "CHECK")}, colliders on player {player.GetComponents<Collider>().Length}, " +
                       $"colliders under visual {visual.GetComponentsInChildren<Collider>(true).Length}");

            EditorSceneManager.MarkSceneDirty(scene);
            if (save)
            {
                bool saved = EditorSceneManager.SaveScene(scene);
                log.Append(saved ? "; saved" : "; SAVE FAILED");
            }
            else log.Append("; NOT saved (save = false)");
            return log.ToString();
        }
    }
}
