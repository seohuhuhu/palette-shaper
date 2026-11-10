using PaletteShaper.Player;
using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>
    /// [임시 테스트용] 3·4단계 확인을 위해 방 하나를 만들어 플레이어를 세운다.
    /// 5단계 RoomManager(방 전환 루프)가 만들어지면 대체된다.
    /// roomIndex 0 = 기믹 테스트 맵, 1~5 = 명세 Room 1~5.
    /// </summary>
    public class DebugRoomLoader : MonoBehaviour
    {
        [SerializeField] PaletteShaperAssets assets;
        [SerializeField] PlayerController2D player;
        [SerializeField, Range(0, 5)] int roomIndex;
        [Tooltip("코어 없이 바로 Q 전환을 허용 (테스트 편의)")]
        [SerializeField] bool unlockSwitchOnStart;

        public static readonly string[] GIMMICK_TEST_MAP =
        {
            "####################",
            "#..................#",
            "#..................#",
            "#..R...............#",
            "#.....G=====.......#",
            "#............XXX...#",
            "#...............CC.#",
            "#........WW........#",
            "#P.*.B...WW.....C.E#",
            "#####...############",
            "####################",
        };

        BuiltRoom room;
        [Tooltip("화면 좌상단에 플레이어/팔레트 상태 표시")]
        [SerializeField] bool showDebugHud = true;
        int jumpPresses, switchPresses;

        void Update()
        {
            if (GameInput.JumpPressed) jumpPresses++;
            if (GameInput.SwitchPressed) switchPresses++;
        }

        void OnGUI()
        {
            if (!showDebugHud || player == null) return;
            var pm = PaletteManager.Instance;
            string text = $"pos {player.transform.position.x:0.00},{player.transform.position.y:0.00}  vel {player.Velocity.x:0.0},{player.Velocity.y:0.0}\n" +
                          $"grounded {player.IsGrounded}  dead {player.IsDead}  jumpKey {jumpPresses}  switchKey {switchPresses}\n" +
                          $"palette {(pm != null ? pm.State.ToString() : "-")}  unlocked {(pm != null && pm.SwitchUnlocked)}\n{player.DebugState}";
            GUI.color = Color.black;
            GUI.Label(new Rect(9, 9, 700, 100), text);
            GUI.color = Color.white;
            GUI.Label(new Rect(8, 8, 700, 100), text);
        }


        void Start() => Build();

        [ContextMenu("Rebuild")]
        public void Build()
        {
            if (room != null && room.Root != null) Destroy(room.Root);
            string[] map = roomIndex == 0 ? GIMMICK_TEST_MAP : GameConstants.ROOM_MAPS[roomIndex - 1];
            room = RoomBuilder.Build(map, assets, transform, roomIndex == 0 ? "Room_GimmickTest" : $"Room_{roomIndex}");
            if (player != null) player.SetSpawnPoint(room.SpawnPoint, true);
            if (room.Exit != null)
                room.Exit.Reached += () => Debug.Log("[PaletteShaper][Debug] 출구 도달 (방 전환은 5단계 RoomManager에서 구현)");
            if (unlockSwitchOnStart && PaletteManager.Instance != null) PaletteManager.Instance.UnlockSwitch();
            Debug.Log($"[PaletteShaper][Debug] {room.Root.name} 생성, 스폰 {room.SpawnPoint}");
        }
    }
}
