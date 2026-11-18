using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PaletteShaper.Core
{
    /// <summary>자동 플레이테스트 등 키보드 대신 입력을 공급하는 소스.</summary>
    public interface IGameInputSource
    {
        float Horizontal { get; }
        bool JumpPressed { get; }
        bool JumpHeld { get; }
        bool JumpReleased { get; }
        /// <summary>Q / 1: CRIMSON 토글</summary>
        bool SwitchPressed { get; }
        /// <summary>E / 2: AMBER 토글</summary>
        bool AmberPressed { get; }
        /// <summary>0: 즉시 MONOCHROME</summary>
        bool OffPressed { get; }
    }

    /// <summary>
    /// 조작 키 매핑: 이동 A/D·←/→, 점프 Space, CRIMSON Q·1, AMBER E·2, 끄기 0. 디버그: N/P = 다음/이전 Room.
    /// Override가 설정되면(자동 플레이테스트) 키보드 대신 그 값을 쓴다.
    /// Active Input Handling 설정(Input System / 구 Input Manager)에 상관없이 동작한다.
    /// </summary>
    public static class GameInput
    {
        public static IGameInputSource Override { get; set; }

        public static float Horizontal => Override != null ? Override.Horizontal : RawHorizontal;
        public static bool JumpPressed => Override != null ? Override.JumpPressed : RawJumpPressed;
        public static bool JumpHeld => Override != null ? Override.JumpHeld : RawJumpHeld;
        public static bool JumpReleased => Override != null ? Override.JumpReleased : RawJumpReleased;
        public static bool SwitchPressed => Override != null ? Override.SwitchPressed : RawSwitchPressed;
        public static bool AmberPressed => Override != null ? Override.AmberPressed : RawAmberPressed;
        public static bool OffPressed => Override != null ? Override.OffPressed : RawOffPressed;

#if ENABLE_INPUT_SYSTEM
        static Keyboard Kb => Keyboard.current;

        static float RawHorizontal
        {
            get
            {
                var kb = Kb;
                if (kb == null) return 0f;
                float h = 0f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
                return h;
            }
        }

        static bool RawJumpPressed => Kb != null && Kb.spaceKey.wasPressedThisFrame;
        static bool RawJumpHeld => Kb != null && Kb.spaceKey.isPressed;
        static bool RawJumpReleased => Kb != null && Kb.spaceKey.wasReleasedThisFrame;
        static bool RawSwitchPressed => Kb != null && (Kb.qKey.wasPressedThisFrame || Kb.digit1Key.wasPressedThisFrame);
        static bool RawAmberPressed => Kb != null && (Kb.eKey.wasPressedThisFrame || Kb.digit2Key.wasPressedThisFrame);
        static bool RawOffPressed => Kb != null && Kb.digit0Key.wasPressedThisFrame;
        public static bool DebugNextRoom => Kb != null && Kb.nKey.wasPressedThisFrame;
        public static bool DebugPrevRoom => Kb != null && Kb.pKey.wasPressedThisFrame;
#else
        static float RawHorizontal
        {
            get
            {
                float h = 0f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
                return h;
            }
        }

        static bool RawJumpPressed => Input.GetKeyDown(KeyCode.Space);
        static bool RawJumpHeld => Input.GetKey(KeyCode.Space);
        static bool RawJumpReleased => Input.GetKeyUp(KeyCode.Space);
        static bool RawSwitchPressed => Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Alpha1);
        static bool RawAmberPressed => Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Alpha2);
        static bool RawOffPressed => Input.GetKeyDown(KeyCode.Alpha0);
        public static bool DebugNextRoom => Input.GetKeyDown(KeyCode.N);
        public static bool DebugPrevRoom => Input.GetKeyDown(KeyCode.P);
#endif
    }
}
