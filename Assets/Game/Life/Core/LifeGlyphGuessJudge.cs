using System;
using System.Collections;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public enum LifeGlyphGuessStatus { Accepted, Rejected, Unavailable }

    public sealed class LifeGlyphGuessResult
    {
        public LifeGlyphGuessStatus Status;
        public string glyphId, guess, feedback;
    }

    public sealed class LifeGlyphGuessJudge
    {
        private readonly GlyphCatalog catalog;
        private readonly Func<JudgementInput, Action<JudgementResult>, Func<bool>, IEnumerator> judge;

        public LifeGlyphGuessJudge(LanguageJudgementClient client, GlyphCatalog catalog)
            : this(catalog, client == null ? null : new Func<JudgementInput, Action<JudgementResult>, Func<bool>, IEnumerator>(client.Judge)) { }

        internal LifeGlyphGuessJudge(GlyphCatalog catalog, Func<JudgementInput, Action<JudgementResult>, Func<bool>, IEnumerator> judge)
        { this.catalog = catalog; this.judge = judge; }

        public static bool TryAccept(string glyphId, string guess) => LifeGlyphGuessLexicon.TryAccept(glyphId, guess);

        public IEnumerator Judge(string glyphId, string guess, Action<LifeGlyphGuessResult> complete, Func<bool> cancelled = null)
        {
            if (complete == null) throw new ArgumentNullException(nameof(complete));
            Func<bool> isCancelled = cancelled ?? (() => false);
            var definition = Array.Find(catalog?.glyphs ?? Array.Empty<GlyphDefinition>(), item => item != null && item.id == glyphId);
            if (isCancelled() || definition == null || string.IsNullOrWhiteSpace(guess))
            {
                complete(Result(LifeGlyphGuessStatus.Unavailable, glyphId, guess, "这次猜测已保留，尚未确认词义。"));
                yield break;
            }
            if (TryAccept(glyphId, guess))
            {
                complete(Result(LifeGlyphGuessStatus.Accepted, glyphId, guess, "这个意思说得通。"));
                yield break;
            }
            if (judge == null)
            {
                complete(Result(LifeGlyphGuessStatus.Unavailable, glyphId, guess, "语义判断暂不可用，猜测已保留。"));
                yield break;
            }

            JudgementResult response = null;
            var input = new JudgementInput
            {
                animalId = "tutorial", utteranceId = "tutorial-glyph-" + glyphId, glyphIds = new[] { glyphId },
                requestMeaning = definition.meaning, playerReply = guess,
                context = "这是手账中对当前一个字的词义猜测。只判断这次猜测是否表达该字的完整意思，不能仅因包含关键词就接受。" +
                    "否定这个意思、禁止说这个意思、罗列互相矛盾的意思均不证明理解。含糊友好不等于明确的问候。" +
                    "本句字义边界：" + glyphId + "=" + definition.meaning + " " + definition.boundary
            };
            yield return judge(input, result => response = result, isCancelled);
            if (isCancelled() || response == null || !response.success)
                complete(Result(LifeGlyphGuessStatus.Unavailable, glyphId, guess,
                    isCancelled() || response == null || string.IsNullOrWhiteSpace(response.error) ? "语义判断未完成，猜测已保留。" : response.error));
            else if (!response.understood)
                complete(Result(LifeGlyphGuessStatus.Rejected, glyphId, guess, "这个猜测还没有表达出当前字的意思。"));
            else if (response.evidenceGlyphIds != null && response.evidenceGlyphIds.Length == 1 && response.evidenceGlyphIds[0] == glyphId)
                complete(Result(LifeGlyphGuessStatus.Accepted, glyphId, guess, "这个意思说得通。"));
            else
                complete(Result(LifeGlyphGuessStatus.Unavailable, glyphId, guess, "语义判断缺少当前字的明确证据，猜测已保留。"));
        }

        private static LifeGlyphGuessResult Result(LifeGlyphGuessStatus status, string glyphId, string guess, string feedback) =>
            new LifeGlyphGuessResult { Status = status, glyphId = glyphId, guess = guess, feedback = feedback };
    }
}
