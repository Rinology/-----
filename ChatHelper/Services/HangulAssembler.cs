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
}
