using System;
using System.Collections.Generic;
using System.Text;

namespace WeatherHouse.Life
{
    public static class LifeGlyphGuessLexicon
    {
        private static readonly Dictionary<string, HashSet<string>> Expressions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            { "hello", Words("你好|您好|你们好|嗨|嗨呀|哈喽|哈罗|问好|问候|致意|招呼|打招呼|打个招呼|相互问好|互相问好|向对方问好|向人问好|向别人问好|向你问好|跟你问好|和你打招呼|跟你打招呼|向你打招呼|向对方打招呼|跟对方打招呼|向别人打招呼|见面问好|见面打招呼|见面时打招呼|见面时向对方打招呼|初次见面招呼|初次见面打招呼|初次见面时打招呼|见面时问候|见面时向对方问好|表达问候|表示问候|表示问好|表示友好的问候|友好地打招呼|友好的招呼|友好的问候|向对方致意|向对方表示问候|hello|hi|hey|hiya|howdy|greetings|greeting|greet|greet someone|say hello|saying hello|say hi|hello there|good morning|good afternoon|good evening|nice to meet you") },
            { "goodbye", Words("再见|再会|拜拜|拜了|掰掰|回见|回头见|下次见|下回见|待会见|一会见|一会儿见|明天见|告别|道别|说再见|说声再见|挥手告别|挥手道别|相互道别|互相道别|向对方告别|向对方道别|跟你道别|和你道别|跟你说再见|向你告别|分别时道别|分别时说再见|离别时道别|离开时道别|结束交谈时道别|结束当前交谈分别时道别|表示告别|表示道别|告辞|辞别|别了|后会有期|goodbye|good bye|bye|bye bye|farewell|see you|see you later|see you soon|see you next time|see you tomorrow|so long|say goodbye|saying goodbye|bid farewell|parting greeting") }
        };
        private static readonly string[] GuessPrefixes =
        {
            "我猜这个字的意思是", "我觉得这个字的意思是", "这个字的意思是", "这个字表示的是", "这个字表示",
            "它的意思是", "它表示的是", "它表示", "意思应该是", "意思是", "我猜应该是", "我猜是", "我觉得是", "我认为是", "应该是", "表示", "是"
        };

        public static bool TryAccept(string glyphId, string guess)
        {
            HashSet<string> words;
            if (glyphId == null || !Expressions.TryGetValue(glyphId, out words) || string.IsNullOrWhiteSpace(guess)) return false;
            string value = TrimParticles(Normalize(guess));
            if (words.Contains(value)) return true;
            foreach (string prefix in GuessPrefixes)
                if (value.StartsWith(prefix, StringComparison.Ordinal) && words.Contains(TrimParticles(value.Substring(prefix.Length)))) return true;
            return false;
        }

        private static HashSet<string> Words(string expressions)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string expression in expressions.Split('|')) result.Add(Normalize(expression));
            return result;
        }

        private static string Normalize(string text)
        {
            var result = new StringBuilder();
            foreach (char character in text.Normalize(NormalizationForm.FormKC))
                if (!char.IsWhiteSpace(character) && !char.IsPunctuation(character)) result.Append(char.ToLowerInvariant(character));
            return result.ToString();
        }

        private static string TrimParticles(string text)
        {
            const string particles = "啊呀呢吧哦喔啦噢呐吗嘛嗯";
            int start = 0, end = text.Length;
            while (start < end && particles.IndexOf(text[start]) >= 0) start++;
            while (end > start && particles.IndexOf(text[end - 1]) >= 0) end--;
            return text.Substring(start, end - start);
        }
    }
}
