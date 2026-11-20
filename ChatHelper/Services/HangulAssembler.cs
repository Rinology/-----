using System.Collections.Generic;
using System.Text;

namespace ChatHelper.Services;

/// <summary>
/// 영문 QWERTY 물리 키 입력(2벌식)을 한글 음절 문자열로 변환하는 경량 조합기.
/// Chrome/브라우저 등에서 한글 IME 조합 중 물리 키코드만 들어올 때 자연어 검색어 복원에 사용합니다.
/// </summary>
public static class HangulAssembler
{
    private static readonly char[] Choseong =
        { 'ㄱ','ㄲ','ㄴ','ㄷ','ㄸ','ㄹ','ㅁ','ㅂ','ㅃ','ㅅ','ㅆ','ㅇ','ㅈ','ㅉ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ' };

    private static readonly char[] Jungseong =
        { 'ㅏ','ㅐ','ㅑ','ㅒ','ㅓ','ㅔ','ㅕ','ㅖ','ㅗ','ㅘ','ㅙ','ㅚ','ㅛ','ㅜ','ㅝ','ㅞ','ㅟ','ㅠ','ㅡ','ㅢ','ㅣ' };

    private static readonly char[] Jongseong =
        { '\0','ㄱ','ㄲ','ㄳ','ㄴ','ㄵ','ㄶ','ㄷ','ㄹ','ㄺ','ㄻ','ㄼ','ㄽ','ㄾ','ㄿ','ㅀ','ㅁ','ㅂ','ㅄ','ㅅ','ㅆ','ㅇ','ㅈ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ' };

    private static readonly Dictionary<char, char> KeyToJamo = new()
    {
        ['q'] = 'ㅂ', ['Q'] = 'ㅃ', ['w'] = 'ㅈ', ['W'] = 'ㅉ', ['e'] = 'ㄷ', ['E'] = 'ㄸ',
        ['r'] = 'ㄱ', ['R'] = 'ㄲ', ['t'] = 'ㅅ', ['T'] = 'ㅆ', ['y'] = 'ㅛ', ['Y'] = 'ㅛ',
        ['u'] = 'ㅕ', ['U'] = 'ㅕ', ['i'] = 'ㅑ', ['I'] = 'ㅑ', ['o'] = 'ㅐ', ['O'] = 'ㅒ',
        ['p'] = 'ㅔ', ['P'] = 'ㅖ',
        ['a'] = 'ㅁ', ['A'] = 'ㅁ', ['s'] = 'ㄴ', ['S'] = 'ㄴ', ['d'] = 'ㅇ', ['D'] = 'ㅇ',
        ['f'] = 'ㄹ', ['F'] = 'ㄹ', ['g'] = 'ㅎ', ['G'] = 'ㅎ', ['h'] = 'ㅗ', ['H'] = 'ㅗ',
        ['j'] = 'ㅓ', ['J'] = 'ㅓ', ['k'] = 'ㅏ', ['K'] = 'ㅏ', ['l'] = 'ㅣ', ['L'] = 'ㅣ',
        ['z'] = 'ㅋ', ['Z'] = 'ㅋ', ['x'] = 'ㅌ', ['X'] = 'ㅌ', ['c'] = 'ㅊ', ['C'] = 'ㅊ',
        ['v'] = 'ㅍ', ['V'] = 'ㅍ', ['b'] = 'ㅠ', ['B'] = 'ㅠ', ['n'] = 'ㅜ', ['N'] = 'ㅜ',
        ['m'] = 'ㅡ', ['M'] = 'ㅡ'
    };

    private static readonly Dictionary<(char, char), char> CombinedVowels = new()
    {
        [('ㅗ','ㅏ')] = 'ㅘ', [('ㅗ','ㅐ')] = 'ㅙ', [('ㅗ','ㅣ')] = 'ㅚ',
        [('ㅜ','ㅓ')] = 'ㅝ', [('ㅜ','ㅔ')] = 'ㅞ', [('ㅜ','ㅣ')] = 'ㅟ',
        [('ㅡ','ㅣ')] = 'ㅢ'
    };

    private static readonly Dictionary<(char, char), char> CombinedFinals = new()
    {
        [('ㄱ','ㅅ')] = 'ㄳ', [('ㄴ','ㅈ')] = 'ㄵ', [('ㄴ','ㅎ')] = 'ㄶ',
        [('ㄹ','ㄱ')] = 'ㄺ', [('ㄹ','ㅁ')] = 'ㄻ', [('ㄹ','ㅂ')] = 'ㄼ',
        [('ㄹ','ㅅ')] = 'ㄽ', [('ㄹ','ㅌ')] = 'ㄾ', [('ㄹ','ㅍ')] = 'ㄿ',
        [('ㄹ','ㅎ')] = 'ㅀ', [('ㅂ','ㅅ')] = 'ㅄ'
    };

    private static readonly Dictionary<char, (char, char)> SplitFinals = new()
    {
        ['ㄳ'] = ('ㄱ','ㅅ'), ['ㄵ'] = ('ㄴ','ㅈ'), ['ㄶ'] = ('ㄴ','ㅎ'),
        ['ㄺ'] = ('ㄹ','ㄱ'), ['ㄻ'] = ('ㄹ','ㅁ'), ['ㄼ'] = ('ㄹ','ㅂ'),
        ['ㄽ'] = ('ㄹ','ㅅ'), ['ㄾ'] = ('ㄹ','ㅌ'), ['ㄿ'] = ('ㄹ','ㅍ'),
        ['ㅀ'] = ('ㄹ','ㅎ'), ['ㅄ'] = ('ㅂ','ㅅ')
    };

    public static string ConvertQwertyToHangul(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder();
        int cho = -1, jung = -1, jong = 0;

        void Flush()
        {
            if (cho >= 0 && jung >= 0)
            {
                sb.Append((char)(0xAC00 + cho * 21 * 28 + jung * 28 + jong));
            }
            else if (cho >= 0)
            {
                sb.Append(Choseong[cho]);
            }
            else if (jung >= 0)
            {
                sb.Append(Jungseong[jung]);
            }
            cho = -1;
            jung = -1;
            jong = 0;
        }

        foreach (char rawCh in input)
        {
            if (!KeyToJamo.TryGetValue(rawCh, out char jamo))
            {
                Flush();
                sb.Append(rawCh);
                continue;
            }

            int cIdx = System.Array.IndexOf(Choseong, jamo);
            int jIdx = System.Array.IndexOf(Jungseong, jamo);

            if (jIdx >= 0) // 모음
            {
                if (cho >= 0 && jung < 0)
                {
                    jung = jIdx;
                }
                else if (cho >= 0 && jung >= 0 && jong == 0)
                {
                    if (CombinedVowels.TryGetValue((Jungseong[jung], jamo), out char comb))
                    {
                        jung = System.Array.IndexOf(Jungseong, comb);
                    }
                    else
                    {
                        Flush();
                        jung = jIdx;
                    }
                }
                else if (cho >= 0 && jung >= 0 && jong > 0)
                {
                    // 종성 도깨비불 처리
                    char lastJong = Jongseong[jong];
                    if (SplitFinals.TryGetValue(lastJong, out var split))
                    {
                        jong = System.Array.IndexOf(Jongseong, split.Item1);
                        Flush();
                        cho = System.Array.IndexOf(Choseong, split.Item2);
                        jung = jIdx;
                    }
                    else
                    {
                        jong = 0;
                        Flush();
                        cho = System.Array.IndexOf(Choseong, lastJong);
                        jung = jIdx;
                    }
                }
                else
                {
                    Flush();
                    jung = jIdx;
                }
            }
            else if (cIdx >= 0) // 자음
            {
                if (cho < 0)
                {
                    Flush();
                    cho = cIdx;
                }
                else if (jung < 0)
                {
                    Flush();
                    cho = cIdx;
                }
                else if (jong == 0)
                {
                    int newJong = System.Array.IndexOf(Jongseong, jamo);
                    if (newJong > 0)
                    {
                        jong = newJong;
                    }
                    else
                    {
                        Flush();
                        cho = cIdx;
                    }
                }
                else
                {
                    if (CombinedFinals.TryGetValue((Jongseong[jong], jamo), out char combJong))
                    {
                        jong = System.Array.IndexOf(Jongseong, combJong);
                    }
                    else
                    {
                        Flush();
                        cho = cIdx;
                    }
                }
            }
        }

        Flush();
        return sb.ToString();
    }

    /// <summary>
    /// 문자열에서 한글 음절의 초성을 추출합니다. (예: "반품 안내" -> "ㅂㅍ ㅇㄴ")
    /// </summary>
    public static string ExtractChoseong(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c >= 0xAC00 && c <= 0xD7A3)
            {
                int choIdx = (c - 0xAC00) / (21 * 28);
                sb.Append(Choseong[choIdx]);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 검색어가 초성 자음(예: "ㅂㅍ")으로만 구성되어 있는지 확인합니다.
    /// </summary>
    public static bool IsChoseongQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        bool hasChoseong = false;
        foreach (char c in query.Trim())
        {
            if (c == ' ') continue;
            if (System.Array.IndexOf(Choseong, c) >= 0)
            {
                hasChoseong = true;
            }
            else
            {
                return false;
            }
        }
        return hasChoseong;
    }

    /// <summary>
    /// Windows 한국어 2벌식 IME의 실제 조합(Composing) vs 확정(Committed) 상태와 Backspace 동작을 1:1로 재현하는 버퍼.
    /// - 조합 중인 마지막 음절은 Backspace 시 자모 1개씩 삭제
    /// - 이미 확정된 음절(다음 글자로 넘어갔거나 Space 이후)은 Backspace 1회에 음절 전체(자모 2~4개)가 한 번에 삭제됨
    /// </summary>
    public class HangulImeBuffer
    {
        private readonly List<(string Hangul, string Raw)> _committed = new();
        private readonly List<char> _composingRaw = new();

        public int Length => GetRawText().Length;

        public void Clear()
        {
            _committed.Clear();
            _composingRaw.Clear();
        }

        public void CommitCurrent()
        {
            if (_composingRaw.Count == 0) return;
            string raw = new string(_composingRaw.ToArray());
            string hangul = ConvertQwertyToHangul(raw);
            _committed.Add((hangul, raw));
            _composingRaw.Clear();
        }

        public void Append(char rawCh)
        {
            if (!KeyToJamo.ContainsKey(rawCh))
            {
                CommitCurrent();
                string s = rawCh.ToString();
                _committed.Add((s, s));
                TrimToMax(25);
                return;
            }

            _composingRaw.Add(rawCh);
            string compRaw = new string(_composingRaw.ToArray());
            string compHangul = ConvertQwertyToHangul(compRaw);

            // 조합 결과가 2음절 이상이 되면 앞 음절들은 확정(Commit)하고 마지막 음절의 원시 키만 조합 버퍼에 유지
            if (compHangul.Length > 1)
            {
                // 뒤에서부터 마지막 1음절만 남는 분리 지점 탐색
                int splitIdx = _composingRaw.Count - 1;
                for (int k = 1; k < _composingRaw.Count; k++)
                {
                    string prefixHangul = ConvertQwertyToHangul(compRaw[..k]);
                    string suffixHangul = ConvertQwertyToHangul(compRaw[k..]);
                    if (prefixHangul + suffixHangul == compHangul && suffixHangul.Length == 1)
                    {
                        splitIdx = k;
                        break;
                    }
                }

                string committedRaw = compRaw[..splitIdx];
                string committedHangul = ConvertQwertyToHangul(committedRaw);
                for (int i = 0; i < committedHangul.Length; i++)
                {
                    string hChar = committedHangul[i].ToString();
                    string rPart = (i == committedHangul.Length - 1) ? committedRaw : hChar;
                    _committed.Add((hChar, rPart));
                }

                var remaining = compRaw[splitIdx..];
                _composingRaw.Clear();
                _composingRaw.AddRange(remaining);
            }

            TrimToMax(25);
        }

        public void Backspace()
        {
            if (_composingRaw.Count > 0)
            {
                // 조합 중인 글자는 자모 1개만 삭제
                _composingRaw.RemoveAt(_composingRaw.Count - 1);
            }
            else if (_committed.Count > 0)
            {
                // 이미 확정된 글자는 Windows IME와 동일하게 음절 1개 통째로 삭제
                _committed.RemoveAt(_committed.Count - 1);
            }
        }

        public string GetHangulText()
        {
            var sb = new StringBuilder();
            foreach (var (h, _) in _committed)
                sb.Append(h);
            if (_composingRaw.Count > 0)
                sb.Append(ConvertQwertyToHangul(new string(_composingRaw.ToArray())));
            return sb.ToString();
        }

        public string GetRawText()
        {
            var sb = new StringBuilder();
            foreach (var (_, r) in _committed)
                sb.Append(r);
            foreach (char c in _composingRaw)
                sb.Append(c);
            return sb.ToString();
        }

        private void TrimToMax(int maxUnits)
        {
            while (_committed.Count > maxUnits)
            {
                _committed.RemoveAt(0);
            }
        }
    }
}
