using System;
using System.Collections.Generic;

namespace keyshadow
{
    internal sealed class PinyinGuide
    {
        // Syllable inventory: https://github.com/rime/rime-luna-pinyin/blob/master/luna_pinyin.dict.yaml
        // Keep Mandarin spellings; omit dialect/compatibility entries such as biang, fong, wong and lvan.
        // Internally v represents ü; the displayed spelling restores the umlaut for n/l.
        internal const string Syllables =
            "a ai an ang ao " +
            "ba bai ban bang bao bei ben beng bi bian biao bie bin bing bo bu " +
            "ca cai can cang cao ce cen ceng cha chai chan chang chao che chen cheng chi chong chou chu chua chuai chuan chuang chui chun chuo ci cong cou cu cuan cui cun cuo " +
            "da dai dan dang dao de dei den deng di dia dian diao die ding diu dong dou du duan dui dun duo " +
            "e ei en eng er " +
            "fa fan fang fei fen feng fo fou fu " +
            "ga gai gan gang gao ge gei gen geng gong gou gu gua guai guan guang gui gun guo " +
            "ha hai han hang hao he hei hen heng hong hou hu hua huai huan huang hui hun huo " +
            "ji jia jian jiang jiao jie jin jing jiong jiu ju juan jue jun " +
            "ka kai kan kang kao ke ken keng kong kou ku kua kuai kuan kuang kui kun kuo " +
            "la lai lan lang lao le lei leng li lia lian liang liao lie lin ling liu lo long lou lu luan lun luo lv lve " +
            "ma mai man mang mao me mei men meng mi mian miao mie min ming miu mo mou mu " +
            "na nai nan nang nao ne nei nen neng ni nian niang niao nie nin ning niu nong nou nu nuan nuo nv nve " +
            "o ou " +
            "pa pai pan pang pao pei pen peng pi pia pian piao pie pin ping po pou pu " +
            "qi qia qian qiang qiao qie qin qing qiong qiu qu quan que qun " +
            "ran rang rao re ren reng ri rong rou ru rua ruan rui run ruo " +
            "sa sai san sang sao se sen seng sha shai shan shang shao she shei shen sheng shi shou shu shua shuai shuan shuang shui shun shuo si song sou su suan sui sun suo " +
            "ta tai tan tang tao te tei teng ti tian tiao tie ting tong tou tu tuan tui tun tuo " +
            "wa wai wan wang wei wen weng wo wu " +
            "xi xia xian xiang xiao xie xin xing xiong xiu xu xuan xue xun " +
            "ya yan yang yao ye yi yin ying yo yong you yu yuan yue yun " +
            "za zai zan zang zao ze zei zen zeng zha zhai zhan zhang zhao zhe zhei zhen zheng zhi zhong zhou zhu zhua zhuai zhuan zhuang zhui zhun zhuo zi zong zou zu zuan zui zun zuo";

        private readonly PinyinScheme scheme;
        private readonly Dictionary<string, string> codes = new Dictionary<string, string>(StringComparer.Ordinal);

        internal PinyinGuide(PinyinScheme scheme)
        {
            this.scheme = scheme;
            foreach (string syllable in Syllables.Split(' '))
            {
                foreach (string code in scheme.GetCodes(syllable))
                {
                    string spelling = syllable.Replace('v', 'ü');
                    string existing;
                    codes[code] = codes.TryGetValue(code, out existing) ? existing + "/" + spelling : spelling;
                }
            }
        }

        internal string Decode(string twoLowercaseKeys)
        {
            string spelling;
            return twoLowercaseKeys != null && codes.TryGetValue(twoLowercaseKeys, out spelling)
                ? spelling : string.Empty;
        }

        internal bool CanFollow(char firstLowercaseKey, char secondLowercaseKey)
        {
            return codes.ContainsKey(new string(new[] { firstLowercaseKey, secondLowercaseKey }));
        }

        internal string InitialLabel(char key)
        {
            string special = scheme.InitialLabel(key);
            if (special == "零") return "零声母";
            if (special.Length != 0) return special;
            if (key == 'a' || key == 'e' || key == 'o') return key + "（零声母）";
            return key >= 'a' && key <= 'z' ? key.ToString() : string.Empty;
        }
    }
}
