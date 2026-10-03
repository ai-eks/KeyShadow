using System;
using System.Collections.Generic;
using keyshadow;

internal static class SchemeTests
{
    private static int checks;

    private static void Main()
    {
        Check(PinyinScheme.All.Length == 4, "four verified layouts");
        Check(PinyinScheme.FromId(null).Id == "flypy" && PinyinScheme.FromId("unknown").Id == "flypy",
            "missing or obsolete scheme falls back to Xiaohe");
        string[] zeroSyllables = "a ai an ang ao e ei en eng er o ou".Split(' ');
        CheckZero("flypy", zeroSyllables, "aa ai,ad an,aj ah ao,ac ee ei,ew en,ef eg er oo ou,oz");
        CheckZero("natural", zeroSyllables, "aa ai,al an,aj ah ao,ak ee ei,ez en,ef eg er oo ou,ob");
        CheckZero("microsoft", zeroSyllables, "oa,aa ol,al oj,aj oh,ah ok,ak oe,ee oz,ez of,ef og,eg or,er oo ob,ou");
        CheckZero("abc", zeroSyllables, "oa ol oj oh ok oe oq of og or oo ob");

        CheckCodes("flypy", "shuang", "ul");
        CheckCodes("natural", "shuang", "ud");
        CheckCodes("microsoft", "shuang", "ud");
        CheckCodes("abc", "shuang", "vt");
        CheckCodes("abc", "zhong", "as");
        CheckCodes("abc", "cheng", "eg");
        foreach (string id in new[] { "flypy", "natural", "abc" })
        {
            CheckCodes(id, "nv", "nv");
            CheckCodes(id, "lv", "lv");
            CheckCodes(id, "ju", "ju,jv");
            CheckCodes(id, "yu", "yu,yv");
            CheckCodes(id, "nve", id == "abc" ? "nm" : "nt");
            CheckCodes(id, "lve", id == "abc" ? "lm" : "lt");
            CheckCodes(id, "jue", id == "abc" ? "jm" : "jt");
            CheckCodes(id, "yue", id == "abc" ? "ym" : "yt");
        }
        CheckCodes("microsoft", "nv", "ny");
        CheckCodes("microsoft", "lv", "ly");
        CheckCodes("microsoft", "ju", "ju,jy");
        CheckCodes("microsoft", "yu", "yu,yy");
        CheckCodes("microsoft", "nve", "nt,nv");
        CheckCodes("microsoft", "lve", "lt,lv");
        CheckCodes("microsoft", "jue", "jt,jv");
        CheckCodes("microsoft", "yue", "yt,yv");
        CheckCodes("microsoft", "bing", "b;");
        CheckCodes("flypy", "bing", "bk");
        CheckCodes("natural", "bing", "by");
        CheckCodes("abc", "bing", "by");

        string[] syllables = PinyinGuide.Syllables.Split(' ');
        Check(syllables.Length == 411 && new HashSet<string>(syllables).Count == 411,
            "411 unique Mandarin syllables retained");
        foreach (PinyinScheme scheme in PinyinScheme.All)
        {
            var guide = new PinyinGuide(scheme);
            Check(scheme.UsesSemicolon == (scheme.Id == "microsoft"), scheme.Id + " semicolon layout");
            Check(guide.Decode("lo") == "lo/luo", scheme.Id + " colliding spellings retained");
            Check(guide.Decode(null) == "" && guide.Decode("invalid") == "", scheme.Id + " invalid decode");
            Check(guide.InitialLabel('b') == "b" && scheme.InitialLabel('b') == "",
                scheme.Id + " ordinary initials need no extra key label");
            Check(guide.InitialLabel(scheme.Id == "abc" ? 'a' : 'v') == "zh", scheme.Id + " compound initial label");
            int codeCount = 0;
            foreach (string syllable in syllables)
            {
                string[] codes = scheme.GetCodes(syllable);
                Check(codes.Length != 0, scheme.Id + ": missing " + syllable);
                Check(new HashSet<string>(codes).Count == codes.Length, scheme.Id + ": duplicate code for " + syllable);
                foreach (string code in codes)
                {
                    Check(code.Length == 2, scheme.Id + ": non-double code " + code);
                    Check(Array.IndexOf(guide.Decode(code).Split('/'), syllable.Replace('v', 'ü')) >= 0,
                        scheme.Id + ": roundtrip " + syllable);
                    Check(guide.CanFollow(code[0], code[1]), scheme.Id + ": missing next-key hint " + code);
                    Check(scheme.FinalLabel(code[1]).Length != 0, scheme.Id + ": missing final key caption " + code);
                    codeCount++;
                }
            }
            const string alphabet = "abcdefghijklmnopqrstuvwxyz;";
            foreach (char first in alphabet)
                foreach (char second in alphabet)
                {
                    string code = first.ToString() + second;
                    Check(guide.CanFollow(first, second) == (guide.Decode(code).Length != 0),
                        scheme.Id + ": guide/decode disagreement " + code);
                }
            Console.WriteLine("PASS " + scheme.Name + ": 411 syllables, " + codeCount + " spellings, 729 key pairs checked");
        }
        Check(PinyinScheme.FromId("microsoft").FinalLabel(';') == "ing", "semicolon key caption");
        Check(PinyinScheme.FromId("microsoft").FinalLabel('v') == "ui/ue/üe", "Microsoft alternate ue caption");
        Check(PinyinScheme.FromId("abc").FinalLabel('m') == "ui/ue/üe", "ABC ue caption");
        Console.WriteLine("PASS " + checks + " assertions");
    }

    private static void CheckZero(string id, string[] syllables, string expected)
    {
        string[] codes = expected.Split(' ');
        for (int i = 0; i < syllables.Length; i++) CheckCodes(id, syllables[i], codes[i]);
    }

    private static void CheckCodes(string id, string syllable, string expected)
    {
        var actual = new HashSet<string>(PinyinScheme.FromId(id).GetCodes(syllable));
        Check(actual.SetEquals(expected.Split(',')), id + ": unexpected encoding for " + syllable);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL " + name);
        checks++;
    }
}
