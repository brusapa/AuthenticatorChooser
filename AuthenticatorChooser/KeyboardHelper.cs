using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace AuthenticatorChooser
{
    internal static class KeyboardHelper
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern short GetKeyState(int nVirtKey);

        // Virtual key codes for Shift keys
        private const int VK_LSHIFT = 0xA0; // Left shift key
        private const int VK_RSHIFT = 0xA1; // Right shift key

        private static bool IsKeyPressed(int keyCode)
        {
            short state = GetKeyState(keyCode);
            // The 0x8000 bit indicates if the key is pressed
            return (state & 0x8000) != 0;
        }

        public static bool IsShiftPressed()
        {
            return IsKeyPressed(VK_LSHIFT) || IsKeyPressed(VK_RSHIFT);
        }
    }
}
