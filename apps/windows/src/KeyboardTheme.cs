using System.Drawing;

namespace keyshadow
{
    internal sealed class KeyboardTheme
    {
        internal string Id, Name;
        internal Color Background, Ink, Muted, Final, Initial, Border, Button, ButtonHover;
        internal Color Key, SpecialKey, SpecialBorder, ActiveKey, NextKey, KeyBorder, Guide;

        internal static readonly KeyboardTheme[] All = {
            new KeyboardTheme {
                Id = "navy", Name = "深海蓝",
                Background = C(0x171F2B), Ink = C(0xEDF4F6), Muted = C(0x9CADBC),
                Final = C(0x89E4D1), Initial = C(0xFAC882), Border = C(0x3F4E5E),
                Button = C(0x24303E), ButtonHover = C(0x3E4D5E), Key = C(0x232E3C),
                SpecialKey = C(0x303131), SpecialBorder = C(0x705E44), ActiveKey = C(0x3D746A),
                NextKey = C(0x254342), KeyBorder = C(0x384757), Guide = C(0x1D2A36)
            },
            new KeyboardTheme {
                Id = "graphite", Name = "石墨灰",
                Background = C(0x242426), Ink = C(0xF3F3F4), Muted = C(0xBAB9C1),
                Final = C(0xA9D2FF), Initial = C(0xF1C591), Border = C(0x54545B),
                Button = C(0x35353B), ButtonHover = C(0x484851), Key = C(0x303036),
                SpecialKey = C(0x3A342E), SpecialBorder = C(0x9D805C), ActiveKey = C(0x375676),
                NextKey = C(0x2D3E54), KeyBorder = C(0x52525B), Guide = C(0x2B2C32)
            },
            new KeyboardTheme {
                Id = "cream", Name = "暖米白",
                Background = C(0xF3EEE5), Ink = C(0x302D29), Muted = C(0x655E54),
                Final = C(0x176158), Initial = C(0x804A10), Border = C(0xC5BCAF),
                Button = C(0xE4DCCE), ButtonHover = C(0xD5CABB), Key = C(0xFFFCF7),
                SpecialKey = C(0xF2E4CF), SpecialBorder = C(0xC4A67D), ActiveKey = C(0xADD6C9),
                NextKey = C(0xD8EAE0), KeyBorder = C(0xCCC3B6), Guide = C(0xE6E1D7)
            },
            new KeyboardTheme {
                Id = "lavender", Name = "薰衣紫",
                Background = C(0x282337), Ink = C(0xF6F0FF), Muted = C(0xBFB1CF),
                Final = C(0xD2BAFF), Initial = C(0xF5C997), Border = C(0x5E5076),
                Button = C(0x3B314F), ButtonHover = C(0x514367), Key = C(0x352D47),
                SpecialKey = C(0x433745), SpecialBorder = C(0xA08472), ActiveKey = C(0x594975),
                NextKey = C(0x483A62), KeyBorder = C(0x635376), Guide = C(0x302A43)
            }
        };

        internal static KeyboardTheme Find(string id)
        {
            foreach (KeyboardTheme theme in All) if (theme.Id == id) return theme;
            return All[0];
        }

        private static Color C(int rgb)
        {
            return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }
    }
}
