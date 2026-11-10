using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PaletteShaper.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PaletteShaper.EditorTools
{
    /// <summary>
    /// 1) Assets/_Project/Sprites 하위 텍스처 임포트 규격 강제 (PPU 16 / Point / Uncompressed / Strip 자동 Slice)
    /// 2) 메뉴: 프로젝트 기반 세팅(폴더, Sorting/Physics 레이어, PlayerSettings, VCS ignore), MainScene 생성
    /// 모든 작업은 여러 번 실행해도 결과가 같도록(idempotent) 작성됨.
    /// </summary>
    public partial class ProjectSetupProcessor : AssetPostprocessor
    {
        public const string ProjectRoot = "Assets/_Project";
        public const string SpritesRoot = "Assets/_Project/Sprites";
        public const string MainScenePath = "Assets/_Project/Scenes/MainScene.unity";
        const string LogTag = "[PaletteShaper]";

        static readonly string[] Folders =
        {
            "Assets/_Project",
            "Assets/_Project/Data",
            "Assets/_Project/Sprites",
            "Assets/_Project/Audio",
            "Assets/_Project/Materials",
            "Assets/_Project/Prefabs",
            "Assets/_Project/Scenes",
            "Assets/_Project/Scripts",
            "Assets/_Project/Scripts/Editor",
            "Assets/_Project/Scripts/Core",
            "Assets/_Project/Scripts/Player",
            "Assets/_Project/Scripts/Gimmicks",
        };

        static readonly string[] VcsIgnoreLines =
        {
            "/Library/", "/Temp/", "/Obj/", "/obj/", "/Build/", "/Builds/", "/Logs/", "/UserSettings/", "/MemoryCaptures/",
            "*.csproj", "*.sln", ".vs/", ".idea/",
            // Visual Studio 자동 생성물 (2026-11 추가)
            "*.slnx", "/Backup/", "UpgradeLog*.htm"
        };

        static readonly Regex StripRegex = new Regex(@"_Strip(\d+)$", RegexOptions.Compiled);

        // ───────────────────────── Texture Import ─────────────────────────

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(SpritesRoot + "/")) return;

            var importer = (TextureImporter)assetImporter;
            string fileName = Path.GetFileNameWithoutExtension(path);
            bool isPlayer = fileName.StartsWith("Spr_Player_");
            int frames = ParseStripFrames(fileName);

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.textureType = TextureImporterType.Sprite;
            settings.spriteMode = frames > 1 ? (int)SpriteImportMode.Multiple : (int)SpriteImportMode.Single;
            settings.spritePixelsPerUnit = GameConstants.PIXELS_PER_UNIT;
            settings.filterMode = FilterMode.Point;
            settings.mipmapEnabled = false;
            settings.alphaIsTransparency = true;
            settings.wrapMode = TextureWrapMode.Clamp;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            settings.spriteAlignment = (int)(isPlayer ? SpriteAlignment.BottomCenter : SpriteAlignment.Center);
            settings.spritePivot = isPlayer ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
            importer.SetTextureSettings(settings);

            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;

            if (frames > 1)
                SliceStrip(importer, fileName, frames, isPlayer);
        }

        static int ParseStripFrames(string fileName)
        {
            var m = StripRegex.Match(fileName);
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > 0 ? n : 1;
        }

        static void SliceStrip(TextureImporter importer, string fileName, int frames, bool isPlayer)
        {
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (width <= 0 || height <= 0) return; // 소스 크기를 아직 알 수 없음
            if (width % frames != 0)
            {
                Debug.LogError($"{LogTag} {fileName}: 가로 {width}px 가 프레임 수 {frames}로 나누어떨어지지 않습니다. Slice 건너뜀.");
                return;
            }

            int frameWidth = width / frames;
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
            dataProvider.InitSpriteEditorDataProvider();

            // 기존 spriteID를 이름 기준으로 재사용해 참조(애니메이션 등)가 끊기지 않게 함
            var existing = dataProvider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
            var rects = new SpriteRect[frames];
            for (int i = 0; i < frames; i++)
            {
                string name = $"{fileName}_{i}";
                rects[i] = new SpriteRect
                {
                    name = name,
                    rect = new Rect(i * frameWidth, 0, frameWidth, height),
                    alignment = isPlayer ? SpriteAlignment.BottomCenter : SpriteAlignment.Center,
                    pivot = isPlayer ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                    spriteID = existing.TryGetValue(name, out var id) ? id : GUID.Generate(),
                };
            }

            dataProvider.SetSpriteRects(rects);
            var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameFileIdProvider != null)
                nameFileIdProvider.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
            dataProvider.Apply();
        }

        // ───────────────────────── Menu: Project Settings ─────────────────────────

        [MenuItem("Palette Shaper/1. Setup Project Settings", priority = 1)]
        public static void SetupProjectSettings()
        {
            var log = new StringBuilder($"{LogTag} 1단계 프로젝트 세팅 결과\n");
            CreateFolders(log);
            SetupSortingLayers(log);
            SetupPhysicsLayers(log);
            SetupPlayerSettings(log);
            WriteVcsIgnoreFiles(log);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(log.ToString());
        }

        static void CreateFolders(StringBuilder log)
        {
            int created = 0;
            foreach (var folder in Folders)
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
                created++;
            }
            log.AppendLine($"- 폴더: {Folders.Length}개 확인, {created}개 새로 생성");
        }

        static SerializedObject LoadTagManager() =>
            new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

        static int FindSortingLayer(SerializedProperty layers, string name)
        {
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name)
                    return i;
            return -1;
        }

        static void SetupSortingLayers(StringBuilder log)
        {
            var tagManager = LoadTagManager();
            var layers = tagManager.FindProperty("m_SortingLayers");

            var usedIds = new HashSet<int>();
            for (int i = 0; i < layers.arraySize; i++)
                usedIds.Add(layers.GetArrayElementAtIndex(i).FindPropertyRelative("uniqueID").intValue);

            foreach (var name in GameConstants.SORTING_LAYER_ORDER)
            {
                if (FindSortingLayer(layers, name) >= 0) continue;
                int idx = layers.arraySize;
                layers.InsertArrayElementAtIndex(idx);
                var element = layers.GetArrayElementAtIndex(idx);
                element.FindPropertyRelative("name").stringValue = name;
                int id;
                do { id = Random.Range(1, int.MaxValue); } while (usedIds.Contains(id));
                usedIds.Add(id);
                element.FindPropertyRelative("uniqueID").intValue = id;
                var locked = element.FindPropertyRelative("locked");
                if (locked != null) locked.boolValue = false;
            }

            // Default를 맨 뒤(0번)로, 이후 명세 순서대로 정렬
            int def = FindSortingLayer(layers, GameConstants.SORT_DEFAULT);
            if (def > 0) layers.MoveArrayElement(def, 0);
            for (int i = 0; i < GameConstants.SORTING_LAYER_ORDER.Length; i++)
            {
                int cur = FindSortingLayer(layers, GameConstants.SORTING_LAYER_ORDER[i]);
                if (cur != i + 1) layers.MoveArrayElement(cur, i + 1);
            }
            tagManager.ApplyModifiedProperties();

            var order = new List<string>();
            for (int i = 0; i < layers.arraySize; i++)
                order.Add($"{i}:{layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue}");
            log.AppendLine("- Sorting Layers: " + string.Join(" → ", order));
        }

        static void SetupPhysicsLayers(StringBuilder log)
        {
            var tagManager = LoadTagManager();
            var layers = tagManager.FindProperty("layers");
            var wanted = new Dictionary<int, string>
            {
                { GameConstants.LAYER_PLAYER, GameConstants.LAYER_NAME_PLAYER },
                { GameConstants.LAYER_TERRAIN_BASE, GameConstants.LAYER_NAME_TERRAIN_BASE },
                { GameConstants.LAYER_CRIMSON_SOLID, GameConstants.LAYER_NAME_CRIMSON_SOLID },
                { GameConstants.LAYER_CRIMSON_HAZARD, GameConstants.LAYER_NAME_CRIMSON_HAZARD },
            };
            foreach (var kv in wanted)
            {
                var prop = layers.GetArrayElementAtIndex(kv.Key);
                if (string.IsNullOrEmpty(prop.stringValue) || prop.stringValue == kv.Value)
                {
                    prop.stringValue = kv.Value;
                    log.AppendLine($"- Physics Layer {kv.Key}: {kv.Value}");
                }
                else
                {
                    log.AppendLine($"- [경고] Physics Layer {kv.Key}가 이미 '{prop.stringValue}'로 사용 중 → 덮어쓰지 않음");
                }
            }
            tagManager.ApplyModifiedProperties();
        }

        static void SetupPlayerSettings(StringBuilder log)
        {
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            // 창이 비활성이어도 게임 시간이 멈추지 않도록 (자동 플레이테스트·디버깅 편의)
            PlayerSettings.runInBackground = true;
            log.AppendLine($"- PlayerSettings.fullScreenMode = {PlayerSettings.fullScreenMode}");
        }

        /// <summary>.gitignore / .p4ignore 를 명세 목록으로 덮어쓴다 (이 도구가 관리하는 파일).</summary>
        static void WriteVcsIgnoreFiles(StringBuilder log)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            var sb = new StringBuilder();
            sb.AppendLine("# Palette Shaper - Unity VCS ignore (ProjectSetupProcessor가 관리, 직접 수정 시 메뉴 재실행으로 덮어써짐)");
            foreach (var line in VcsIgnoreLines) sb.AppendLine(line);
            foreach (var file in new[] { ".gitignore", ".p4ignore" })
            {
                string path = Path.Combine(root, file);
                File.WriteAllText(path, sb.ToString());
                log.AppendLine($"- {file}: {VcsIgnoreLines.Length}줄 작성 ({path})");
            }
        }

        // ───────────────────────── Menu: MainScene ─────────────────────────



        static void SetupMainCamera(StringBuilder log)
        {
            var camGo = GameObject.FindWithTag("MainCamera");
            if (camGo == null)
            {
                camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            }

            var cam = camGo.GetComponent<Camera>();
            if (cam == null) cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;
            // 방 바깥/레터박스 = Deep Charcoal. 방 안쪽 빈 공간은 RoomBuilder의 BG_Wall(Warm Gray)이 채움.
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = GameConstants.DEEP_CHARCOAL;

            // 방 좌하단 타일 모서리를 (0,0)으로 두는 기준. 방 중심 = (10, 5.5)
            camGo.transform.position = new Vector3(GameConstants.ROOM_WIDTH_TILES * 0.5f, GameConstants.ROOM_HEIGHT_TILES * 0.5f, -10f);

            if (camGo.GetComponent<AudioListener>() == null) camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData();

            var ppc = camGo.GetComponent<PixelPerfectCamera>();
            if (ppc == null) ppc = camGo.AddComponent<PixelPerfectCamera>();
            ppc.assetsPPU = GameConstants.PIXELS_PER_UNIT;
            ppc.refResolutionX = GameConstants.REF_RESOLUTION_X;
            ppc.refResolutionY = GameConstants.REF_RESOLUTION_Y;
            ppc.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            // 창 크기에 맞춰 16:9를 유지한 채 꽉 채움 (1920x1080에서는 정확히 6배 정수 배율)
            ppc.cropFrame = PixelPerfectCamera.CropFrame.StretchFill;

            log.AppendLine($"- Main Camera: pos {camGo.transform.position}, PixelPerfectCamera PPU {ppc.assetsPPU}, Ref {ppc.refResolutionX}x{ppc.refResolutionY}, gridSnapping {ppc.gridSnapping}, cropFrame {ppc.cropFrame}");
        }

        static void SetupGlobalLight(StringBuilder log)
        {
            var existing = Object.FindObjectsByType<Light2D>()
                .FirstOrDefault(l => l.lightType == Light2D.LightType.Global);
            if (existing != null)
            {
                log.AppendLine("- Global Light 2D: 이미 있음");
                return;
            }
            var go = new GameObject("Global Light 2D");
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            log.AppendLine("- Global Light 2D: 생성");
        }

        static void AddMainSceneToBuild(StringBuilder log)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == MainScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(MainScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            log.AppendLine("- Build Settings: " + string.Join(", ", scenes.Select((s, i) => $"{i}:{Path.GetFileNameWithoutExtension(s.path)}")));
        }

        // ───────────────────────── Menu: Validation ─────────────────────────

        [MenuItem("Palette Shaper/Debug/Validate Sprite Import Pipeline", priority = 100)]
        public static void ValidateSpriteImport()
        {
            const string testFolder = SpritesRoot + "/_ValidateTmp";
            if (!AssetDatabase.IsValidFolder(SpritesRoot)) CreateFolders(new StringBuilder());
            if (!AssetDatabase.IsValidFolder(testFolder)) AssetDatabase.CreateFolder(SpritesRoot, "_ValidateTmp");

            string stripPath = testFolder + "/Spr_Player_ValidateTmp_Strip2.png"; // 48x24, 2프레임, Bottom-Center
            string singlePath = testFolder + "/Spr_Tile_ValidateTmp.png";          // 16x16, 단일, Center
            WriteTestPng(stripPath, 48, 24);
            WriteTestPng(singlePath, 16, 16);
            AssetDatabase.ImportAsset(stripPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(singlePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var results = new List<string>();
            bool allPass = true;
            void Check(string label, bool ok, string detail)
            {
                allPass &= ok;
                results.Add($"{(ok ? "PASS" : "FAIL")} {label} ({detail})");
            }

            foreach (var p in new[] { stripPath, singlePath })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                string n = Path.GetFileName(p);
                Check($"{n} PPU", ti.spritePixelsPerUnit == 16f, ti.spritePixelsPerUnit.ToString());
                Check($"{n} Filter", ti.filterMode == FilterMode.Point, ti.filterMode.ToString());
                Check($"{n} Compression", ti.textureCompression == TextureImporterCompression.Uncompressed, ti.textureCompression.ToString());
                Check($"{n} Type", ti.textureType == TextureImporterType.Sprite, ti.textureType.ToString());
            }

            var stripSprites = AssetDatabase.LoadAllAssetsAtPath(stripPath).OfType<Sprite>().OrderBy(s => s.name).ToArray();
            Check("Strip2 슬라이스 수", stripSprites.Length == 2, stripSprites.Length.ToString());
            foreach (var s in stripSprites)
            {
                Check($"{s.name} 크기", s.rect.width == 24 && s.rect.height == 24, $"{s.rect.width}x{s.rect.height}");
                Check($"{s.name} Pivot Bottom-Center", s.pivot == new Vector2(12f, 0f), s.pivot.ToString());
            }
            var single = AssetDatabase.LoadAllAssetsAtPath(singlePath).OfType<Sprite>().ToArray();
            Check("단일 스프라이트 수", single.Length == 1, single.Length.ToString());
            if (single.Length == 1)
            {
                Check("단일 Pivot Center", single[0].pivot == new Vector2(8f, 8f), single[0].pivot.ToString());
                Check("단일 bounds 1x1 Unit", Mathf.Approximately(single[0].bounds.size.x, 1f) && Mathf.Approximately(single[0].bounds.size.y, 1f), single[0].bounds.size.ToString());
            }

            AssetDatabase.DeleteAsset(testFolder);
            string msg = $"{LogTag}[Validate] {(allPass ? "ALL PASS" : "FAILED")}\n" + string.Join("\n", results);
            if (allPass) Debug.Log(msg); else Debug.LogError(msg);
        }

        static void WriteTestPng(string assetPath, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = GameConstants.CREAM_IVORY;
            tex.SetPixels32(px);
            tex.Apply();
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
