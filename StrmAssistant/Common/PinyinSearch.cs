using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TinyPinyin;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 中文搜索增强里的「模糊搜索」和「拼音搜索」的匹配逻辑（纯函数，不碰 Emby，便于单测）。
    ///
    /// Emby 自带的搜索只按「词」的开头匹配：「星际」能搜到「星际穿越」，「穿越」「际穿」就搜不到；
    /// 标题里有「·」「：」之类符号时，连写的「哈利波特」也搜不到；拼音完全不支持。
    ///
    /// 这里的规则：
    ///   · 先规整：繁体转简体、全角转半角、转小写、去掉空格和标点。
    ///   · 模糊：规整后的查询词出现在标题任意位置即算命中；空格分开的多个词要全部命中（顺序不限）。
    ///   · 拼音：全拼（xingjichuanyue）、首字母（xjcy）、混写（xingjcy / 星际cy）都能匹配，也可以从标题中间开始；
    ///     多音字（长、重、行、乐……）的各个读音都算。
    /// </summary>
    public static class PinyinSearch
    {
        /// <summary>常见多音字的其它读音（TinyPinyin 每个字只给一个读音）。</summary>
        private static readonly Dictionary<char, string[]> Polyphones = BuildPolyphones();

        private static Dictionary<char, string[]> BuildPolyphones()
        {
            // 字 → 所有读音（无声调，ü 写作 v）
            var raw = new Dictionary<char, string>
            {
                ['长'] = "chang,zhang", ['重'] = "zhong,chong", ['行'] = "xing,hang", ['乐'] = "le,yue",
                ['还'] = "hai,huan", ['传'] = "chuan,zhuan", ['了'] = "le,liao", ['朝'] = "chao,zhao",
                ['藏'] = "cang,zang", ['降'] = "jiang,xiang", ['调'] = "diao,tiao", ['曾'] = "zeng,ceng",
                ['便'] = "bian,pian", ['都'] = "dou,du", ['弹'] = "dan,tan", ['少'] = "shao",
                ['着'] = "zhe,zhao,zhuo", ['角'] = "jiao,jue", ['和'] = "he,huo,hu", ['解'] = "jie,xie",
                ['薄'] = "bo,bao", ['单'] = "dan,shan,chan", ['地'] = "di,de", ['得'] = "de,dei",
                ['干'] = "gan", ['间'] = "jian", ['将'] = "jiang", ['落'] = "luo,la,lao",
                ['没'] = "mei,mo", ['模'] = "mo,mu", ['南'] = "nan,na", ['强'] = "qiang,jiang",
                ['区'] = "qu,ou", ['省'] = "sheng,xing", ['识'] = "shi,zhi", ['属'] = "shu,zhu",
                ['说'] = "shuo,shui", ['宿'] = "su,xiu", ['系'] = "xi,ji", ['吓'] = "xia,he",
                ['血'] = "xue,xie", ['只'] = "zhi", ['种'] = "zhong", ['转'] = "zhuan",
                ['奇'] = "qi,ji", ['大'] = "da,dai", ['给'] = "gei,ji", ['卡'] = "ka,qia",
                ['差'] = "cha,chai,ci", ['参'] = "can,shen,cen", ['剥'] = "bo,bao", ['露'] = "lu,lou",
                ['绿'] = "lv,lu", ['率'] = "lv,shuai", ['柏'] = "bai,bo", ['秘'] = "mi,bi",
                ['查'] = "cha,zha", ['尉'] = "wei,yu", ['仇'] = "chou,qiu", ['盖'] = "gai,ge",
                ['覃'] = "qin,tan", ['朴'] = "pu,piao", ['车'] = "che,ju", ['茄'] = "qie,jia",
                ['厦'] = "sha,xia", ['会'] = "hui,kuai", ['觉'] = "jue,jiao", ['看'] = "kan",
                ['空'] = "kong", ['乘'] = "cheng,sheng", ['冲'] = "chong", ['处'] = "chu",
                ['当'] = "dang", ['倒'] = "dao", ['发'] = "fa", ['分'] = "fen",
                ['好'] = "hao", ['几'] = "ji", ['假'] = "jia", ['教'] = "jiao",
                ['结'] = "jie", ['累'] = "lei", ['量'] = "liang", ['难'] = "nan",
                ['片'] = "pian", ['曲'] = "qu", ['散'] = "san", ['舍'] = "she",
                ['数'] = "shu,shuo", ['为'] = "wei", ['相'] = "xiang", ['兴'] = "xing",
                ['应'] = "ying", ['与'] = "yu", ['折'] = "zhe,she", ['正'] = "zheng",
                ['中'] = "zhong", ['钻'] = "zuan", ['作'] = "zuo", ['塞'] = "sai,se",
                ['色'] = "se,shai", ['似'] = "si,shi", ['提'] = "ti,di", ['校'] = "xiao,jiao",
                ['咽'] = "yan,ye", ['钥'] = "yao,yue", ['尾'] = "wei,yi", ['否'] = "fou,pi",
                ['伯'] = "bo,bai", ['番'] = "fan,pan", ['恶'] = "e,wu", ['给'] = "gei,ji",
                ['哈'] = "ha", ['华'] = "hua", ['叶'] = "ye,xie", ['员'] = "yuan,yun",
                ['乾'] = "qian,gan", ['阿'] = "a,e", ['蔓'] = "man,wan", ['莎'] = "sha,suo",
                ['期'] = "qi,ji", ['什'] = "shen,shi", ['吐'] = "tu", ['扎'] = "zha,za",
                ['仔'] = "zai,zi", ['娜'] = "na,nuo", ['畜'] = "chu,xu", ['殷'] = "yin,yan",
                ['燕'] = "yan", ['佛'] = "fo,fu", ['龟'] = "gui,jun,qiu", ['拓'] = "tuo,ta",
                ['沈'] = "shen,chen", ['翟'] = "zhai,di", ['缪'] = "miao,mou,miu", ['区'] = "qu,ou",
                ['任'] = "ren", ['雀'] = "que,qiao", ['熟'] = "shu,shou", ['匙'] = "chi,shi",
                ['传'] = "chuan,zhuan", ['泊'] = "bo,po", ['堡'] = "bao,pu", ['膀'] = "bang,pang",
            };

            var map = new Dictionary<char, string[]>();
            foreach (var kv in raw)
            {
                var readings = kv.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                var main = Main(kv.Key);
                if (main != null && !readings.Contains(main)) readings.Add(main);
                map[kv.Key] = readings.Distinct().ToArray();
            }

            return map;
        }

        private static string Main(char c)
        {
            if (!PinyinHelper.IsChinese(c)) return null;
            var p = PinyinHelper.GetPinyin(c);
            return string.IsNullOrEmpty(p) ? null : p.ToLowerInvariant().Replace('ü', 'v');
        }

        private static readonly string[][] Cache = new string[0x9FA5 - 0x4E00 + 1][];
        private static readonly string[] None = Array.Empty<string>();

        /// <summary>一个汉字的所有读音（小写、无声调）；非汉字返回空数组。</summary>
        public static string[] Readings(char c)
        {
            if (c < 0x4E00 || c > 0x9FA5) return Polyphones.TryGetValue(c, out var p) ? p : None;

            var slot = c - 0x4E00;
            var cached = Cache[slot];
            if (cached != null) return cached;

            string[] result;
            if (Polyphones.TryGetValue(c, out var poly)) result = poly;
            else
            {
                var main = Main(c);
                result = main == null ? None : new[] { main };
            }

            Cache[slot] = result;
            return result;
        }

        public static bool IsHan(char c) => c >= 0x3400 && c <= 0x9FFF;

        /// <summary>规整：繁转简、全角转半角、小写、只留字母数字和汉字。</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            string s;
            try { s = text.Normalize(NormalizationForm.FormKC); }
            catch { s = text; }

            if (ChineseTextHelper.ContainsChinese(s))
            {
                try { s = ChineseTextHelper.ToSimplified(s); }
                catch { /* 转换失败就按原文 */ }
            }

            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            }

            return sb.ToString();
        }

        /// <summary>把查询拆成关键词（按空白分），每个都规整过；空的丢掉。</summary>
        public static string[] Keywords(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return Array.Empty<string>();
            return term.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(k => k.Length > 0)
                .Distinct()
                .ToArray();
        }

        /// <summary>这个查询值得做模糊/拼音匹配吗（太短的词会命中一大片，没有意义）。</summary>
        public static bool Worth(string[] keywords) => keywords != null && keywords.Length > 0 && keywords.Sum(k => k.Length) >= 2;

        /// <summary>文本里出现过的所有字母（含汉字读音里的字母），26 位掩码，用来快速排除。</summary>
        public static int LetterMask(string normalized)
        {
            var mask = 0;
            if (string.IsNullOrEmpty(normalized)) return mask;

            foreach (var ch in normalized)
            {
                if (ch >= 'a' && ch <= 'z') mask |= 1 << (ch - 'a');
                else if (IsHan(ch))
                {
                    foreach (var r in Readings(ch))
                        foreach (var rc in r)
                            if (rc >= 'a' && rc <= 'z') mask |= 1 << (rc - 'a');
                }
            }

            return mask;
        }

        private static bool HasAsciiLetter(string s)
        {
            foreach (var ch in s)
                if (ch >= 'a' && ch <= 'z') return true;
            return false;
        }

        private static bool HasHan(string s)
        {
            foreach (var ch in s)
                if (IsHan(ch)) return true;
            return false;
        }

        /// <summary>
        /// 拼音匹配：查询词 keyword（规整过，可含汉字和字母）能否从 text 的某个位置开始连续匹配。
        /// 每个汉字可以用它任一读音的前缀去对（首字母、全拼、半截都行）。
        /// 返回匹配起点，匹配不上返回 -1。
        /// </summary>
        public static int PinyinIndexOf(string text, string keyword)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword)) return -1;
            if (!HasAsciiLetter(keyword) || !HasHan(text)) return -1;

            var n = keyword.Length;
            var m = text.Length;
            // failed[qi, si]：从 (qi, si) 出发已经确认匹配不上
            var failed = new bool[(n + 1) * (m + 1)];

            for (var start = 0; start < m; start++)
            {
                if (Match(text, keyword, 0, start, failed, m)) return start;
            }

            return -1;
        }

        private static bool Match(string s, string q, int qi, int si, bool[] failed, int m)
        {
            if (qi == q.Length) return true;
            if (si == s.Length) return false;

            var key = qi * (m + 1) + si;
            if (failed[key]) return false;

            var c = s[si];
            var qc = q[qi];

            if (c == qc && Match(s, q, qi + 1, si + 1, failed, m)) return true;

            if (qc >= 'a' && qc <= 'z' && IsHan(c))
            {
                foreach (var r in Readings(c))
                {
                    for (var k = 1; k <= r.Length && qi + k <= q.Length; k++)
                    {
                        if (q[qi + k - 1] != r[k - 1]) break;
                        if (Match(s, q, qi + k, si + 1, failed, m)) return true;
                    }
                }
            }

            failed[key] = true;
            return false;
        }

        /// <summary>
        /// 一个关键词对一段规整文本的匹配分（0 = 不匹配）。
        /// 100 完全相同 / 90 开头 / 80 中间出现 / 70 拼音从开头匹配 / 60 拼音从中间匹配。
        /// </summary>
        public static int ScoreKeyword(string text, int textMask, string keyword, int keywordMask)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword)) return 0;

            var idx = text.IndexOf(keyword, StringComparison.Ordinal);
            if (idx == 0) return text.Length == keyword.Length ? 100 : 90;
            if (idx > 0) return 80;

            if (keyword.Length < 2) return 0;
            if ((keywordMask & ~textMask) != 0) return 0;

            var p = PinyinIndexOf(text, keyword);
            if (p == 0) return 70;
            return p > 0 ? 60 : 0;
        }

        /// <summary>所有关键词都要命中，取最低分；任一不中返回 0。</summary>
        public static int Score(string text, int textMask, string[] keywords, int[] keywordMasks)
        {
            if (string.IsNullOrEmpty(text) || keywords == null || keywords.Length == 0) return 0;

            var min = int.MaxValue;
            for (var i = 0; i < keywords.Length; i++)
            {
                var s = ScoreKeyword(text, textMask, keywords[i], keywordMasks[i]);
                if (s == 0) return 0;
                if (s < min) min = s;
            }

            return min;
        }

        /// <summary>只含字母的掩码（查询词用）。</summary>
        public static int KeywordMask(string keyword)
        {
            var mask = 0;
            foreach (var ch in keyword)
                if (ch >= 'a' && ch <= 'z') mask |= 1 << (ch - 'a');
            return mask;
        }

        /// <summary>便捷：原始标题 vs 原始查询，给单测和调试用。</summary>
        public static int Score(string title, string term)
        {
            var text = Normalize(title);
            var keywords = Keywords(term);
            return Score(text, LetterMask(text), keywords, keywords.Select(KeywordMask).ToArray());
        }
    }
}
