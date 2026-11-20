using System.Collections.Generic;
using System.Linq;
using System.Text;
using PaletteShaper.Core;
using PaletteShaper.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace PaletteShaper.EditorTools
{
    /// <summary>
    /// 3·4단계용 에디터 도구
    /// - 4. Create Runtime Assets: Tile_*.asset, Mat_WaveRing.mat, PaletteShaperAssets.asset 생성/갱신
    /// - Debug/Build Test Scene: 저장하지 않는 테스트 씬(카메라, PaletteManager, Player, DebugRoomLoader) 구성
    /// </summary>
    public partial class ProjectSetupProcessor
    {
        public const string DataRoot = "Assets/_Project/Data";
        public const string TilesRoot = "Assets/_Project/Data/Tiles";
        public const string AssetsDbPath = "Assets/_Project/Data/PaletteShaperAssets.asset";
        public const string WaveMaterialPath = "Assets/_Project/Materials/Mat_WaveRing.mat";
        const string SpriteUnlitShader = "Universal Render Pipeline/2D/Sprite-Unlit-Default";

        [MenuItem("Palette Shaper/4. Create Runtime Assets", priority = 4)]
        public static void CreateRuntimeAssets()
        {
            if (!AssetDatabase.IsValidFolder(DataRoot)) CreateFolders(new StringBuilder());
            if (!AssetDatabase.IsValidFolder(TilesRoot)) AssetDatabase.CreateFolder(DataRoot, "Tiles");

            var errors = new List<string>();
            Sprite One(string name)
            {
                var s = LoadFrames(name).FirstOrDefault();
                if (s == null) errors.Add($"스프라이트 없음: {name}");
                return s;
            }
            Sprite[] Frames(string name, int expected)
            {
                var f = LoadFrames(name);
                if (f.Length != expected) errors.Add($"{name}: 프레임 {f.Length} ≠ {expected}");
                return f;
            }
            Sprite FrameAt(string name, int index)
            {
                var f = LoadFrames(name);
                if (f.Length <= index) { errors.Add($"{name}: {index}번 프레임 없음"); return null; }
                return f[index];
            }

            var db = AssetDatabase.LoadAssetAtPath<PaletteShaperAssets>(AssetsDbPath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<PaletteShaperAssets>();
                AssetDatabase.CreateAsset(db, AssetsDbPath);
            }

            db.playerIdle = Frames("Spr_Player_Idle_Strip4", 4);
            db.playerRun = Frames("Spr_Player_Run_Strip6", 6);
            db.playerJump = Frames("Spr_Player_Jump_Strip2", 2);
            db.playerFall = Frames("Spr_Player_Fall_Strip2", 2);
            db.playerLand = Frames("Spr_Player_Land_Strip3", 3);
            db.playerTurn = Frames("Spr_Player_Turn_Strip3", 3);
            db.playerGhostCrimson = Frames("Spr_Player_Ghost_Crimson_Strip20", 20);
            db.playerGhostAmber = Frames("Spr_Player_Ghost_Amber_Strip20", 20);
            db.playerGhostMono = Frames("Spr_Player_Ghost_Mono_Strip20", 20);

            db.tileBaseStone = CreateOrUpdateTile("Tile_BaseStone", One("Spr_Tile_BaseStone"));
            db.tileBaseStoneEdgeRight = CreateOrUpdateTile("Tile_BaseStoneEdgeRight", One("Spr_Tile_BaseStoneEdgeRight"));
            db.tileBaseStoneEdgeBottom = CreateOrUpdateTile("Tile_BaseStoneEdgeBottom", One("Spr_Tile_BaseStoneEdgeBottom"));
            db.tileBaseStoneEdgeBoth = CreateOrUpdateTile("Tile_BaseStoneEdgeBoth", One("Spr_Tile_BaseStoneEdgeBoth"));
            db.tileCrimsonSolid = CreateOrUpdateTile("Tile_CrimsonSolid", One("Spr_Tile_CrimsonActive"));
            db.tileCrimsonSolidInactive = CreateOrUpdateTile("Tile_CrimsonSolidInactive", One("Spr_Tile_CrimsonInactive"));
            db.tileAmberSolid = CreateOrUpdateTile("Tile_AmberSolid", One("Spr_Tile_AmberActive"));
            db.tileAmberSolidInactive = CreateOrUpdateTile("Tile_AmberSolidInactive", One("Spr_Tile_AmberInactive"));

            db.crimsonCore = Frames("Spr_Gimmick_CrimsonCore_Strip4", 4);
            db.spikeActive = One("Spr_Gimmick_SpikeActive");
            db.spikeInactive = One("Spr_Gimmick_SpikeInactive");
            db.bouncePadInactive = FrameAt("Spr_Gimmick_BouncePad_Strip2", 0);
            db.bouncePadActive = FrameAt("Spr_Gimmick_BouncePad_Strip2", 1);
            db.sideBouncePadInactive = FrameAt("Spr_Gimmick_SideBouncePad_Strip2", 0);
            db.sideBouncePadActive = FrameAt("Spr_Gimmick_SideBouncePad_Strip2", 1);
            db.gearPlatformActive = One("Spr_Gimmick_GearPlatform");
            db.gearPlatformInactive = One("Spr_Gimmick_GearPlatform_Inactive");
            db.railTrack = One("Spr_Gimmick_RailTrack");
            db.amberCore = Frames("Spr_Gimmick_AmberCore_Strip4", 4);
            db.amberSpikeActive = One("Spr_Gimmick_AmberSpikeActive");
            db.amberSpikeInactive = One("Spr_Gimmick_AmberSpikeInactive");
            db.amberUpdraft = Frames("Spr_Gimmick_AmberUpdraft_Strip4", 4);
            db.amberUpdraftInactive = One("Spr_Gimmick_AmberUpdraft_Inactive");
            db.exitDoor = One("Spr_Prop_ExitDoor");
            var bgFrames = Frames("Spr_BG_Wall_Strip4", 4);
            db.bgWallTiles = bgFrames.Select((s, i) => (TileBase)CreateOrUpdateTile($"Tile_BGWall_{i}", s, false)).ToArray();
            db.uiFont = Frames($"Spr_UI_Font_Strip{GameConstants.UI_FONT_GLYPHS.Length}", GameConstants.UI_FONT_GLYPHS.Length);
            db.uiBar = One("Spr_UI_Bar");
            db.uiPaletteGauge = Frames("Spr_UI_PaletteGauge_Strip3", 3);
            db.uiSlotCrimson = Frames("Spr_UI_SlotCrimson_Strip3", 3);
            db.uiSlotAmber = Frames("Spr_UI_SlotAmber_Strip3", 3);
            db.waveMaterial = CreateOrUpdateWaveMaterial(errors);

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            if (errors.Count == 0)
                Debug.Log($"{LogTag}[Runtime] ALL PASS: Tile 12개, Mat_WaveRing, PaletteShaperAssets 갱신");
            else
            {
                Debug.LogError($"{LogTag}[Runtime] FAILED: 문제 {errors.Count}건");
                foreach (var e in errors) Debug.LogError($"{LogTag}[Runtime] {e}");
            }
        }

        static Tile CreateOrUpdateTile(string name, Sprite sprite, bool gridCollider = true)
        {
            string path = $"{TilesRoot}/{name}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            bool isNew = tile == null;
            if (isNew) tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.color = Color.white;
            // 스프라이트 모양과 무관하게 1x1 칸 충돌 (활성/비활성 교체 시 형상 동일). 배경 타일은 충돌 없음
            tile.colliderType = gridCollider ? Tile.ColliderType.Grid : Tile.ColliderType.None;
            tile.flags = TileFlags.LockColor;
            if (isNew) AssetDatabase.CreateAsset(tile, path);
            else EditorUtility.SetDirty(tile);
            return tile;
        }

        static Material CreateOrUpdateWaveMaterial(List<string> errors)
        {
            var shader = Shader.Find(SpriteUnlitShader);
            if (shader == null)
            {
                errors.Add($"셰이더 없음: {SpriteUnlitShader}");
                return null;
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(WaveMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "Mat_WaveRing" };
                AssetDatabase.CreateAsset(mat, WaveMaterialPath);
            }
            else
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        // ───────────── MainScene 최종 구성 ─────────────

        public const string PlayerPrefabPath = "Assets/_Project/Prefabs/Pfb_Player.prefab";

        /// <summary>
        /// MainScene: Main Camera(PixelPerfect 320x180 PPU16, Stretch Fill) + StageHUD, Global Light 2D,
        /// PaletteManager, Pfb_Player, RoomManager(Room 1~5). 여러 번 실행해도 같은 결과.
        /// </summary>
        [MenuItem("Palette Shaper/2. Create or Update MainScene", priority = 2)]
        public static void CreateOrUpdateMainScene()
        {
            if (HasDirtySavedScene()) return;
            if (AssetDatabase.LoadAssetAtPath<PaletteShaperAssets>(AssetsDbPath) == null)
            {
                Debug.LogError($"{LogTag} {AssetsDbPath} 이 없습니다. 먼저 '3. Generate Sprites' → '4. Create Runtime Assets'를 실행하세요.");
                return;
            }
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Scenes")) CreateFolders(new StringBuilder());

            bool isNew = !System.IO.File.Exists(MainScenePath);
            var scene = isNew
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            // 씬 전환 뒤에 로드 (전환 시 참조 없는 에셋이 언로드됨)
            var db = AssetDatabase.LoadAssetAtPath<PaletteShaperAssets>(AssetsDbPath);

            var log = new StringBuilder($"{LogTag} MainScene {(isNew ? "생성" : "갱신")}\n");
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "PaletteManager" || root.name == "Player" || root.name == "RoomManager" || root.name == "DebugRoomLoader" || root.name == "Juice")
                    Object.DestroyImmediate(root);
            }
            SetupMainCamera(log);
            SetupGlobalLight(log);
            var cam = GameObject.FindWithTag("MainCamera").GetComponent<Camera>();
            var oldHud = cam.transform.Find("StageHUD");
            if (oldHud != null) Object.DestroyImmediate(oldHud.gameObject);

            var manager = new GameObject("PaletteManager").AddComponent<PaletteManager>();
            SetObjectRef(manager, "waveMaterial", db.waveMaterial);

            var player = CreateOrUpdatePlayerPrefabInstance(db);
            SetObjectRef(manager, "waveOrigin", player.transform);

            var hudGo = new GameObject("StageHUD");
            hudGo.transform.SetParent(cam.transform, false);
            hudGo.transform.localPosition = new Vector3(0f, 0f, 10f);
            var hud = hudGo.AddComponent<StageHUD>();
            SetObjectRef(hud, "assets", db);
            SetObjectRef(hud, "targetCamera", cam);

            var rooms = new GameObject("RoomManager").AddComponent<RoomManager>();
            SetObjectRef(rooms, "assets", db);
            SetObjectRef(rooms, "player", player);
            SetObjectRef(rooms, "hud", hud);

            // GDD 7.6 / 7.7: Game Juice + 절차적 효과음
            var juiceGo = new GameObject("Juice");
            var juice = juiceGo.AddComponent<JuiceManager>();
            SetObjectRef(juice, "assets", db);
            SetObjectRef(juice, "player", player);
            SetObjectRef(juice, "targetCamera", cam);
            juiceGo.AddComponent<AudioSource>();
            var sfx = juiceGo.AddComponent<ProceduralSoundManager>();
            SetObjectRef(sfx, "player", player);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, MainScenePath);
            AddMainSceneToBuild(log);
            log.AppendLine("- PaletteManager, Pfb_Player, StageHUD(카메라 자식), RoomManager, Juice(JuiceManager+ProceduralSoundManager) 배치 및 참조 연결");
            Debug.Log(log.ToString());
        }

        /// <summary>Pfb_Player.prefab을 만들거나 갱신하고, 씬에 연결된 인스턴스를 돌려준다.</summary>
        static PlayerController2D CreateOrUpdatePlayerPrefabInstance(PaletteShaperAssets db)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs")) CreateFolders(new StringBuilder());
            var player = CreatePlayer(db);
            // 반환값은 프리팹 '에셋'이므로 쓰지 않고, 씬에 연결된 원본 인스턴스를 참조한다
            PrefabUtility.SaveAsPrefabAssetAndConnect(player.gameObject, PlayerPrefabPath, InteractionMode.AutomatedAction);
            return player;
        }

        [MenuItem("Palette Shaper/Debug/Run Auto Playtest (Room 1~5)", priority = 103)]
        public static void RunAutoPlaytest()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError($"{LogTag} Play 모드를 먼저 종료하세요.");
                return;
            }
            if (HasDirtySavedScene()) return;
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            SessionState.SetBool(PlaytestBot.SessionKey, true);
            EditorApplication.EnterPlaymode();
        }

        // ───────────── Debug: 테스트 씬 ─────────────

        [MenuItem("Palette Shaper/Debug/Build Test Scene (unsaved)", priority = 102)]
        public static void BuildTestScene()
        {
            if (HasDirtySavedScene()) return;
            var db = AssetDatabase.LoadAssetAtPath<PaletteShaperAssets>(AssetsDbPath);
            if (db == null)
            {
                Debug.LogError($"{LogTag} {AssetsDbPath} 이 없습니다. 먼저 '4. Create Runtime Assets'를 실행하세요.");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // NewScene이 참조 없는 에셋을 언로드하므로 새 씬을 만든 뒤 다시 로드해야 참조가 끊기지 않는다
            db = AssetDatabase.LoadAssetAtPath<PaletteShaperAssets>(AssetsDbPath);
            SetupMainCamera(new StringBuilder());
            SetupGlobalLight(new StringBuilder());

            var manager = new GameObject("PaletteManager").AddComponent<PaletteManager>();
            SetObjectRef(manager, "waveMaterial", db.waveMaterial);

            var player = CreatePlayer(db);
            SetObjectRef(manager, "waveOrigin", player.transform);

            var loader = new GameObject("DebugRoomLoader").AddComponent<DebugRoomLoader>();
            SetObjectRef(loader, "assets", db);
            SetObjectRef(loader, "player", player);

            Selection.activeGameObject = loader.gameObject;
            Debug.Log($"{LogTag}[Debug] 테스트 씬 구성 완료 (저장 안 됨). Play를 누르면 DebugRoomLoader가 방을 생성합니다.");
        }

        /// <summary>Player(Rigidbody2D + BoxCollider2D + PlayerController2D) + 자식 Visual(SpriteRenderer + PlayerAnimator)</summary>
        public static PlayerController2D CreatePlayer(PaletteShaperAssets db)
        {
            var go = new GameObject("Player") { layer = GameConstants.LAYER_PLAYER };
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<BoxCollider2D>();
            var controller = go.AddComponent<PlayerController2D>();

            var visual = new GameObject("Visual") { layer = GameConstants.LAYER_PLAYER };
            visual.transform.SetParent(go.transform, false);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = db.playerIdle != null && db.playerIdle.Length > 0 ? db.playerIdle[0] : null;
            sr.sortingLayerName = GameConstants.SORT_PLAYER;
            var animator = visual.AddComponent<PlayerAnimator>();

            SetObjectRef(controller, "visual", visual.transform);
            SetObjectRef(animator, "controller", controller);
            SetObjectRef(animator, "assets", db);
            return controller;
        }

        static void SetObjectRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"{LogTag} {target.GetType().Name}.{field} 필드를 찾지 못했습니다.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>저장된 씬 중 변경 사항이 있으면 true + 에러 로그. 저장 안 된 임시(Untitled) 씬은 버려도 됨.</summary>
        static bool HasDirtySavedScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isDirty && !string.IsNullOrEmpty(s.path))
                {
                    Debug.LogError($"{LogTag} '{s.name}' 씬에 저장되지 않은 변경이 있어 중단했습니다. 저장하거나 되돌린 뒤 다시 실행하세요.");
                    return true;
                }
            }
            return false;
        }
    }
}
