using System.Collections;
using PaletteShaper.Player;
using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>
    /// Room 1 → 10 진행 루프 (Chapter 1 = Room 1~5 CRIMSON, Chapter 2 = Room 6~10 AMBER).
    /// - ROOM_MAPS를 RoomBuilder로 생성, 플레이어를 P에 배치, 팔레트는 MONOCHROME으로 시작
    /// - 출구(E) 도달 → 0.4초 후 다음 Room, 마지막 Room 클리어 시 ALL CLEAR (Space로 처음부터)
    /// - 사망 후 리스폰 시 팔레트를 MONOCHROME으로 초기화 (그 Room에서 얻은 해금은 유지)
    /// - 해금: Room 1 = 둘 다 잠김(* 로 Q 해금), Room 2~6 = Q만(Room 6의 @ 로 E 해금), Room 7~ = Q·E 둘 다
    /// - 에디터/개발 빌드: N = 다음 Room, P = 이전 Room (테스트용)
    /// </summary>
    public class RoomManager : MonoBehaviour
    {
        public static RoomManager Instance { get; private set; }

        [SerializeField] PaletteShaperAssets assets;
        [SerializeField] PlayerController2D player;
        [SerializeField] StageHUD hud;
        [SerializeField, Range(1, 10)] int startRoom = 1;

        public int CurrentRoom { get; private set; }
        public int RoomCount => GameConstants.ROOM_MAPS.Length;
        public string StageLabel => StageLabelOf(CurrentRoom);
        public static string StageLabelOf(int room) =>
            $"STAGE {(room - 1) / GameConstants.CHAPTER_SIZE + 1}-{(room - 1) % GameConstants.CHAPTER_SIZE + 1}";
        /// <summary>현재 Room의 출구에 도달했는지 (다음 Room 대기 중 또는 ALL CLEAR)</summary>
        public bool ExitReached => transitioning || allClear;
        public bool IsAllClear => allClear;

        /// <summary>출구에 도달한 순간 (사운드용)</summary>
        public event System.Action RoomCleared;

        BuiltRoom room;
        bool transitioning;
        bool allClear;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (player != null) player.Respawned -= OnPlayerRespawned;
        }

        void Start()
        {
            if (player != null) player.Respawned += OnPlayerRespawned;
            LoadRoom(startRoom);
        }

        public void LoadRoom(int number)
        {
            StopAllCoroutines();
            number = Mathf.Clamp(number, 1, RoomCount);
            if (room != null && room.Root != null) Destroy(room.Root);

            room = RoomBuilder.Build(GameConstants.ROOM_MAPS[number - 1], assets, transform, $"Room_{number}", number);
            CurrentRoom = number;
            transitioning = false;
            allClear = false;
            if (room.Exit != null) room.Exit.Reached += OnExitReached;

            var pm = PaletteManager.Instance;
            if (pm != null)
            {
                pm.ResetToMonochrome();
                pm.SetUnlocks(number > 1, number > GameConstants.AMBER_UNLOCK_ROOM);
            }

            if (player != null)
            {
                player.SetSpawnPoint(room.SpawnPoint, true);
                if (player.IsDead) player.Respawn();
            }

            if (hud != null)
            {
                hud.SetStage(StageLabel);
                hud.ShowCenterMessage("");
            }
            Debug.Log($"[PaletteShaper] {StageLabel} 시작");
        }

        void OnExitReached()
        {
            if (transitioning) return;
            transitioning = true;
            RoomCleared?.Invoke();
            StartCoroutine(AdvanceAfterDelay());
        }

        IEnumerator AdvanceAfterDelay()
        {
            yield return new WaitForSeconds(GameConstants.ROOM_TRANSITION_DELAY);
            if (CurrentRoom >= RoomCount)
            {
                allClear = true;
                if (hud != null)
                {
                    hud.SetStage("CLEAR");
                    hud.ShowCenterMessage("ALL CLEAR!");
                }
                Debug.Log("[PaletteShaper] ALL CLEAR");
                yield break;
            }
            LoadRoom(CurrentRoom + 1);
        }

        void OnPlayerRespawned()
        {
            if (PaletteManager.Instance != null) PaletteManager.Instance.ResetToMonochrome();
        }

        void Update()
        {
            if (allClear && GameInput.JumpPressed)
            {
                LoadRoom(1);
                return;
            }
            if (!Debug.isDebugBuild) return; // 에디터·개발 빌드에서만 Room 이동 단축키
            if (GameInput.DebugNextRoom) LoadRoom(CurrentRoom % RoomCount + 1);
            else if (GameInput.DebugPrevRoom) LoadRoom((CurrentRoom + RoomCount - 2) % RoomCount + 1);
        }
    }
}
