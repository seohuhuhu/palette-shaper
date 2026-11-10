using System.Linq;
using System.Text;
using PaletteShaper.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PaletteShaper.EditorTools
{
    /// <summary>
    /// 디버그용: 저장하지 않는 임시 씬에 생성된 스프라이트를 모두 배치해 PixelPerfectCamera로 확인한다.
    /// 확인 후에는 "Palette Shaper/2. Create or Update MainScene"으로 MainScene에 복귀하면 된다.
    /// </summary>
    public partial class ProjectSetupProcessor
    {
        [MenuItem("Palette Shaper/Debug/Open Sprite Preview (unsaved scene)", priority = 101)]
        public static void OpenSpritePreview()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError($"{LogTag} 저장되지 않은 씬이 있어 프리뷰를 열지 않았습니다.");
                    return;
                }
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupMainCamera(new StringBuilder());
            SetupGlobalLight(new StringBuilder());
            var root = new GameObject("SpritePreview").transform;

            // Row A: Player Idle(4) + Run(6), bottom pivot
            float x = 1f;
            x = PlaceFrames(root, "Spr_Player_Idle_Strip4", x, 8.5f, 1.5f, GameConstants.SORT_PLAYER);
            x = PlaceFrames(root, "Spr_Player_Run_Strip6", x + 0.5f, 8.5f, 1.5f, GameConstants.SORT_PLAYER);

            // Row B: Jump(2) + Fall(2), Core(4), BouncePad(2)
            x = PlaceFrames(root, "Spr_Player_Jump_Strip2", 1f, 5.5f, 1.5f, GameConstants.SORT_PLAYER);
            x = PlaceFrames(root, "Spr_Player_Fall_Strip2", x + 0.5f, 5.5f, 1.5f, GameConstants.SORT_PLAYER);
            x = PlaceFrames(root, "Spr_Gimmick_CrimsonCore_Strip4", x + 1f, 6f, 1.25f, GameConstants.SORT_GIMMICK_ACTIVE);
            PlaceFrames(root, "Spr_Gimmick_BouncePad_Strip2", x + 0.5f, 6f, 1.5f, GameConstants.SORT_GIMMICK_ACTIVE);

            // Row C: 타일 이음새 확인용 블록 (각 2x2)
            PlaceGrid(root, "Spr_Tile_BaseStone", 1.5f, 2.5f, 3, 2, GameConstants.SORT_TERRAIN_BASE);
            PlaceGrid(root, "Spr_Tile_CrimsonActive", 5.5f, 2.5f, 2, 2, GameConstants.SORT_GIMMICK_ACTIVE);
            PlaceGrid(root, "Spr_Tile_CrimsonInactive", 8.5f, 2.5f, 2, 2, GameConstants.SORT_GIMMICK_INACTIVE);
            PlaceGrid(root, "Spr_Gimmick_SpikeActive", 11.5f, 2.5f, 2, 1, GameConstants.SORT_GIMMICK_ACTIVE);
            PlaceGrid(root, "Spr_Gimmick_SpikeInactive", 11.5f, 3.5f, 2, 1, GameConstants.SORT_GIMMICK_INACTIVE);
            PlaceGrid(root, "Spr_Gimmick_RailTrack", 11.5f, 0.5f, 4, 1, GameConstants.SORT_BG_PROPS);
            PlaceGrid(root, "Spr_Gimmick_GearPlatform", 12.5f, 0.5f, 1, 1, GameConstants.SORT_GIMMICK_ACTIVE);
            PlaceGrid(root, "Spr_Prop_ExitDoor", 17.5f, 3f, 1, 1, GameConstants.SORT_BG_PROPS);

            Debug.Log($"{LogTag}[Preview] 스프라이트 {root.childCount}개 배치 (저장하지 않는 임시 씬)");
        }

        static Sprite[] LoadFrames(string name)
        {
            return AssetDatabase.LoadAllAssetsAtPath($"{SpritesRoot}/{name}.png").OfType<Sprite>()
                .OrderBy(s => { int u = s.name.LastIndexOf('_'); return int.TryParse(s.name.Substring(u + 1), out int n) ? n : 0; })
                .ToArray();
        }

        static float PlaceFrames(Transform root, string name, float startX, float y, float step, string sortingLayer)
        {
            float x = startX;
            foreach (var s in LoadFrames(name))
            {
                Spawn(root, s, new Vector3(x, y, 0f), sortingLayer);
                x += step;
            }
            return x;
        }

        static void PlaceGrid(Transform root, string name, float x0, float y0, int cols, int rows, string sortingLayer)
        {
            var s = LoadFrames(name).FirstOrDefault();
            if (s == null) { Debug.LogError($"{LogTag}[Preview] {name} 없음"); return; }
            float w = s.bounds.size.x, h = s.bounds.size.y;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    Spawn(root, s, new Vector3(x0 + c * w, y0 + r * h, 0f), sortingLayer);
        }

        static void Spawn(Transform root, Sprite s, Vector3 pos, string sortingLayer)
        {
            var go = new GameObject(s.name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingLayerName = sortingLayer;
        }
    }
}
