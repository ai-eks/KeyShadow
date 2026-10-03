using System;
using System.Collections.Generic;

namespace keyshadow
{
    internal sealed class PinyinScheme
    {
        // Layout facts checked against the Rime project; see docs/双拼方案.md.
        // The same final table supplies both key captions and accepted spellings.
        internal static readonly PinyinScheme[] All =
        {
            new PinyinScheme("flypy", "小鹤双拼", "viu", ZeroStyle.Preserve,
                "q:iu w:ei e:e r:uan t:ue/ve y:un u:u i:i o:uo/o p:ie " +
                "a:a s:ong/iong d:ai f:en g:eng h:ang j:an k:ing/uai l:iang/uang " +
                "z:ou x:ia/ua c:ao v:ui/v b:in n:iao m:ian",
                "零声母：aa / ee / oo；两字母原样；ang → ah，eng → eg",
                "ü → V；ue / üe → T；j / q / x / y 的 u 也可用 V",
                "shuang → ul · 零声母另有兼容编码，需输入法支持（见说明）"),
            new PinyinScheme("natural", "自然码", "viu", ZeroStyle.Preserve,
                "q:iu w:ia/ua e:e r:uan t:ue/ve y:ing/uai u:u i:i o:uo/o p:un " +
                "a:a s:ong/iong d:iang/uang f:en g:eng h:ang j:an k:ao l:ai " +
                "z:ei x:ie c:iao v:ui/v b:ou n:in m:ian",
                "零声母：aa / ee / oo；两字母原样；ang → ah，eng → eg",
                "ü → V；ue / üe → T；j / q / x / y 的 u 也可用 V",
                "shuang → ud · 零声母另有兼容编码，需输入法支持（见说明）"),
            new PinyinScheme("microsoft", "微软双拼", "viu", ZeroStyle.PrefixOAndVowel,
                "q:iu w:ia/ua e:e r:uan/er t:ue/ve y:uai/v u:u i:i o:uo/o p:un " +
                "a:a s:ong/iong d:iang/uang f:en g:eng h:ang j:an k:ao l:ai " +
                "z:ei x:ie c:iao v:ui/ue/ve b:ou n:in m:ian ;:ing",
                "零声母：O + 韵母键，如 a → oa，ai → ol，er → or",
                "ü → Y；ue / üe → T 或 V；ing → 分号键 ;",
                "shuang → ud · A / E 前缀和 ou 兼容编码需输入法支持"),
            new PinyinScheme("abc", "智能 ABC", "aev", ZeroStyle.PrefixO,
                "q:ei w:ian e:e r:iu/er t:iang/uang y:ing u:u i:i o:uo/o p:uan " +
                "a:a s:ong/iong d:ia/ua f:en g:eng h:ang j:an k:ao l:ai " +
                "z:iao x:ie c:in/uai v:v b:ou n:un m:ui/ue/ve",
                "零声母：O + 韵母键，如 a → oa，ai → ol，er → or",
                "zh / ch / sh → A / E / V；ü → V；ue / üe → M",
                "shuang → vt · j / q / x / y 的 u 也可用 V")
        };

        internal readonly string Id, Name, ZeroInitialHint, UmlautHint, ExampleHint;
        private readonly string compoundInitials;
        private readonly ZeroStyle zeroStyle;
        private readonly Dictionary<string, string> finalKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<char, string> finalLabels = new Dictionary<char, string>();

        private enum ZeroStyle { Preserve, PrefixO, PrefixOAndVowel }

        private PinyinScheme(string id, string name, string initials, ZeroStyle zero,
            string layout, string zeroHint, string umlautHint, string exampleHint)
        {
            Id = id; Name = name; compoundInitials = initials; zeroStyle = zero;
            ZeroInitialHint = zeroHint; UmlautHint = umlautHint; ExampleHint = exampleHint;
            foreach (string entry in layout.Split(' '))
            {
                char key = entry[0];
                string[] finals = entry.Substring(2).Split('/');
                var labels = new List<string>();
                foreach (string final in finals)
                {
                    string keys;
                    finalKeys[final] = finalKeys.TryGetValue(final, out keys) ? keys + key : key.ToString();
                    labels.Add(final.Replace("ve", "üe").Replace("v", "ü"));
                }
                finalLabels[key] = string.Join("/", labels.ToArray());
            }
        }

        internal static PinyinScheme FromId(string id)
        {
            foreach (PinyinScheme scheme in All)
                if (string.Equals(scheme.Id, id, StringComparison.Ordinal)) return scheme;
            return All[0];
        }

        internal bool UsesSemicolon { get { return finalLabels.ContainsKey(';'); } }

        internal string FinalLabel(char key)
        {
            string label;
            return finalLabels.TryGetValue(key, out label) ? label : string.Empty;
        }

        internal string InitialLabel(char key)
        {
            int index = compoundInitials.IndexOf(key);
            if (index >= 0) return new[] { "zh", "ch", "sh" }[index];
            if (zeroStyle != ZeroStyle.Preserve && key == 'o') return "零";
            return string.Empty;
        }

        internal string[] GetCodes(string syllable)
        {
            var codes = new List<string>();
            if (string.IsNullOrEmpty(syllable)) return codes.ToArray();
            syllable = syllable.Replace('ü', 'v');
            char first = syllable[0];
            if (first == 'a' || first == 'e' || first == 'o')
            {
                if (zeroStyle == ZeroStyle.Preserve)
                {
                    // Preserve the usual two-letter spelling, with Rime's prefix aliases.
                    if (syllable.Length == 2) codes.Add(syllable);
                    AddCodes(codes, first, syllable);
                }
                else
                {
                    AddCodes(codes, 'o', syllable);
                    if (zeroStyle == ZeroStyle.PrefixOAndVowel)
                    {
                        if (first == 'a' || first == 'e') AddCodes(codes, first, syllable);
                        if (syllable == "ou") codes.Add("ou");
                    }
                }
            }
            else
            {
                int initialLength = 1;
                if (syllable.StartsWith("zh", StringComparison.Ordinal) ||
                    syllable.StartsWith("ch", StringComparison.Ordinal) ||
                    syllable.StartsWith("sh", StringComparison.Ordinal))
                {
                    first = compoundInitials[syllable[0] == 'z' ? 0 : syllable[0] == 'c' ? 1 : 2];
                    initialLength = 2;
                }
                string final = syllable.Substring(initialLength);
                AddCodes(codes, first, final);
                // In the cited schemas ju/qu/xu/yu can use either u or the ü key.
                if (final == "u" && "jqxy".IndexOf(syllable[0]) >= 0) AddCodes(codes, first, "v");
            }
            return codes.ToArray();
        }

        private void AddCodes(List<string> codes, char initial, string final)
        {
            string keys;
            if (!finalKeys.TryGetValue(final, out keys)) return;
            foreach (char key in keys)
            {
                string code = initial.ToString() + key;
                if (!codes.Contains(code)) codes.Add(code);
            }
        }
    }
}
