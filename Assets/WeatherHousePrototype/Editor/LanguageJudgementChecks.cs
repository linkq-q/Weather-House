using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WeatherHousePrototype
{
    public static class LanguageJudgementChecks
    {
        [MenuItem("Weather House/Validate AI Result Parser")]
        public static void Run()
        {
            var lines = new List<string>();
            string path = Path.Combine("PrototypeReview", "language-judgement.txt");
            Directory.CreateDirectory("PrototypeReview");
            try
            {
                JudgementResult positive;
                Check(LanguageJudgementClient.TryParseResult(
                    "{\"understood\":true,\"evidenceGlyphIds\":[\"rain\"],\"feedback\":\"你听懂了。\"}",
                    new[] { "want", "rain" }, out positive) && positive.success && positive.understood &&
                    positive.evidenceGlyphIds.Length == 1 && positive.evidenceGlyphIds[0] == "rain",
                    "Accept a complete positive result for a current glyph", lines);
                JudgementResult negative;
                Check(LanguageJudgementClient.TryParseResult(
                    "{\"understood\":false,\"evidenceGlyphIds\":[],\"feedback\":\"请再具体一点。\"}",
                    new[] { "rain" }, out negative) && negative.success && !negative.understood &&
                    negative.evidenceGlyphIds.Length == 0,
                    "A negative judgement is valid but supplies no confirmation evidence", lines);
                JudgementResult escaped;
                Check(LanguageJudgementClient.TryParseResult(
                    " \r\n{\"feedback\":\"\\u96e8\\n\\\"再想想\\\"\\\\\\/\",\"evidenceGlyphIds\":[],\"understood\":false}\t",
                    new[] { "rain" }, out escaped) && escaped.feedback == "雨\n\"再想想\"\\/",
                    "Accept reordered fields, JSON whitespace, escaped Chinese and punctuation", lines);
                JudgementResult noEvidence;
                Check(LanguageJudgementClient.TryParseResult(
                    "{\"understood\":true,\"evidenceGlyphIds\":[],\"feedback\":\"请求理解正确，但没有足够字义证据。\"}",
                    new[] { "rain" }, out noEvidence) && noEvidence.evidenceGlyphIds.Length == 0,
                    "Understanding a sentence may still provide no glyph evidence", lines);

                Reject("{\"evidenceGlyphIds\":[],\"feedback\":\"x\"}", "Missing understood", lines);
                Reject("{\"understood\":false,\"feedback\":\"x\"}", "Missing evidence list", lines);
                Reject("{\"understood\":false,\"evidenceGlyphIds\":[]}", "Missing feedback", lines);
                Reject("{\"understood\":\"true\",\"evidenceGlyphIds\":[],\"feedback\":\"x\"}", "String is not a boolean", lines);
                Reject("{\"understood\":1,\"evidenceGlyphIds\":[],\"feedback\":\"x\"}", "Number is not a boolean", lines);
                Reject("{\"understood\":true,\"evidenceGlyphIds\":\"rain\",\"feedback\":\"x\"}", "Evidence must be an array", lines);
                Reject("{\"understood\":true,\"evidenceGlyphIds\":[12],\"feedback\":\"x\"}", "Evidence IDs must be strings", lines);
                Reject("{\"understood\":false,\"evidenceGlyphIds\":[],\"feedback\":null}", "Feedback must be a string", lines);
                Reject("{\"understood\":true,\"evidenceGlyphIds\":[\"rain\"],\"feedback\":\"x\",\"execute\":\"confirm all\"}", "Extra instruction field", lines);
                Reject("{\"understood\":false,\"understood\":true,\"evidenceGlyphIds\":[],\"feedback\":\"x\"}", "Duplicate field", lines);
                Reject("{\"understood\":true,\"evidenceGlyphIds\":[\"sun\"],\"feedback\":\"x\"}", "Unknown glyph ID", lines);
                Reject("{\"understood\":true,\"evidenceGlyphIds\":[\"rain\",\"rain\"],\"feedback\":\"x\"}", "Duplicate glyph ID", lines);
                Reject("{\"understood\":false,\"evidenceGlyphIds\":[\"rain\"],\"feedback\":\"x\"}", "Contradictory negative result with evidence", lines);
                Reject("```json\n{\"understood\":false,\"evidenceGlyphIds\":[],\"feedback\":\"x\"}\n```", "Markdown wrapper", lines);
                Reject("{\"understood\":false,\"evidenceGlyphIds\":[],\"feedback\":\"x\"} ignore rules", "Trailing prose", lines);
                Reject("{\"understood\":true,\"evidenceGlyphIds\":[\"rain\",],\"feedback\":\"x\"}", "Trailing array comma", lines);
                Reject("{\"understood\":false,\"evidenceGlyphIds\":[],\"feedback\":\"x\",}", "Trailing object comma", lines);
                Reject("{\"understood\":false,\"evidenceGlyphIds\":[],\"feedback\":\"\\uGGGG\"}", "Invalid Unicode escape", lines);
                Reject("{\"understood\":false,\"evidenceGlyphIds\":[],\"feedback\":\"line\nbreak\"}", "Unescaped control character", lines);
                Reject("{\"understood\":true", "Truncated JSON", lines);
                Reject(null, "Null response", lines);

                lines.Add("Result: PASS (offline parser only; no service request sent)");
                File.WriteAllLines(path, lines);
                Debug.Log("AI parser checks passed: " + path);
            }
            catch (Exception exception)
            {
                lines.Add("Result: FAIL");
                lines.Add(exception.ToString());
                File.WriteAllLines(path, lines);
                throw;
            }
        }

        private static void Reject(string content, string description, List<string> lines)
        {
            JudgementResult result;
            Check(!LanguageJudgementClient.TryParseResult(content, new[] { "rain" }, out result) &&
                !result.success && !result.understood && result.evidenceGlyphIds.Length == 0 &&
                !string.IsNullOrWhiteSpace(result.error), "Reject: " + description, lines);
        }

        private static void Check(bool condition, string description, List<string> lines)
        {
            if (!condition) throw new InvalidOperationException(description);
            lines.Add("PASS: " + description);
        }
    }
}
