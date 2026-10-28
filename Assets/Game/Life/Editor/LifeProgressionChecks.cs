using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.Life.Editor
{
    public static class LifeProgressionChecks
    {
        [Serializable] sealed class Result { public string status; public string[] checks; public string boundary; }
        [MenuItem("Weather House/Life/Check event progression")]
        public static void Run()
        {
            var passed = new List<string>();
            Action<bool, string> check = (ok, label) => {
                if (!ok) throw new InvalidOperationException("LifeProgression: " + label);
                passed.Add(label);
            };
            LifeRequest[] content = LifeContent.Build();
            LifeRequest Find(string id) => content.Single(r => r.id == id);
            var progress = new LifeProgress();
            check(progress.CanPresent(Find("dog.hello"), content), "新档可交谈问候");
            check(!progress.CanPresent(Find("dog.rain.lesson"), content), "未听取问候前不跳入施雨教学");
            check(!progress.CanPresent(Find("dog.water"), content) && !progress.CanPresent(Find("squirrel.mat"), content), "新档不提前发布后续主任务");
            check(progress.CanPresent(Find("squirrel.sequence"), content) && progress.CanPresent(Find("fox.sequence"), content), "摄影两步教学可提前参与，不由主线进度锁住");
            check(new[]{"sheep.self.player","sheep.self.deer","deer.self.sheep","sheep.here","fox.go"}.All(id=>progress.CanPresent(Find(id),content)),"I01至I03公开关系与短路演示无需先通主线");
            check(new[] { "fox.home", "squirrel.home", "sheep.cold", "fox.cold", "sheep.warm", "fox.warm", "dog.hello", "fox.hello", "dog.bye", "fox.bye" }
                .All(id => progress.CanPresent(Find(id), content)), "P11及P18至P21的全部公开语境可在新档先到当地时展示");
            foreach (string id in new[] { "fox.home", "sheep.warm", "fox.warm" })
            {
                LifeRequest request = Find(id); var help = new LifeProgress();
                check(request.HasTask && !help.Hear(request, "photo-context-check", false, true) &&
                    !help.Hear(request, "photo-context-check", true, false) &&
                    help.tasks.Count == 0 && help.heardRequests.Count == 0,
                    "开放摄影话轮不会把过期或远处听取接为帮助：" + id);
                check(help.Hear(request, "photo-context-check", true, true) &&
                    !help.Hear(request, "photo-context-check", true, true) && help.tasks.Count == 1,
                    "当前近处话轮听取后仍只接受一次真实帮助：" + id);
            }
            check(new[] { "dog.sun", "fox.sun", "sheep.strong", "deer.wind", "squirrel.grass", "squirrel.wet", "squirrel.dry" }
                .All(id => progress.CanPresent(Find(id), content)), "提前探索仍有跨动物日照风与材料观察证据");
            string initialHint = progress.GetProgressHint(content);
            check(!initialHint.Contains("河岛") && !initialHint.Contains("先雨后"), "初始提示不公开结尾目标或解法");
            progress.heardRequests.Add("dog.hello");
            check(progress.CanPresent(Find("dog.rain.lesson"), content), "听取问候后开放雨教学");
            check(!progress.CanPresent(Find("dog.water"), content), "只听取问候不算完成雨教学");
            CompleteEvent(progress, content, "E0");
            check(progress.CanPresent(Find("dog.water"), content) && !progress.CanPresent(Find("squirrel.mat"), content), "E0完成仅推进到接水主任务");
            CompleteEvent(progress, content, "E1");
            check(progress.CanPresent(Find("squirrel.mat"), content) && !progress.CanPresent(Find("sheep.wind"), content), "E1完成开放窝垫，后期主任务仍等待");
            CompleteEvent(progress, content, "E2");
            check(new[] { "squirrel.tree", "sheep.wind", "sheep.warm", "sheep.self.player", "sheep.here", "sheep.go" }
                .All(id => progress.CanPresent(Find(id), content)), "E2后林地牧场与两段海岸事件并行");
            check(!progress.EventComplete("E6a", content) && progress.CanPresent(Find("sheep.here"), content), "可先去潮池，不强制E6a在E6b之前");
            check(!progress.EventComplete("E7", content), "开放两步公开教学不会自动完成结尾帮助");
            foreach (string id in new[] { "E3", "E4", "E5", "E6a" }) CompleteEvent(progress, content, id);
            check(!progress.EventComplete("E7", content), "缺少实际帮助结果时结尾仍未完成");
            CompleteEvent(progress, content, "E6b");
            check(progress.CanPresent(Find("squirrel.sequence"), content), "生活事件完成后开放结尾，无全词典门槛");
            check(progress.GetProgressHint(content).Contains("河岛"), "当前提示随生活事件推进");
            CompleteEvent(progress, content, "E7");
            check(progress.GetProgressHint(content).Contains("回访"), "结尾后保留自由回访提示");

            var returning = new LifeProgress();
            returning.heardRequests.Add("fox.sequence");
            returning.tasks.Add(new LifeTask { requestId = "squirrel.sequence", status = LifeTaskStatus.Active });
            check(returning.CanPresent(Find("fox.sequence"), content) && returning.CanPresent(Find("squirrel.sequence"), content), "已有听取或任务永久保留回访，兼容已有存档");
            var holder = new GameObject("Life progression isolated check") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                LifeSpeaker speaker = holder.AddComponent<LifeSpeaker>();
                speaker.requests = content.Where(r => r.speakerKey == "squirrel").ToArray();
                speaker.ConfigureProgress(progress, content);
                speaker.topicIndex = Array.FindIndex(speaker.requests, r => r.id == "squirrel.mat");
                check(speaker.Current.id == "squirrel.mat", "已完成夹具可选择生活帮助话轮");
                int before = speaker.Revision;
                var fresh = new LifeProgress();
                speaker.ConfigureProgress(fresh, content);
                check(speaker.Current.id != "squirrel.mat" && speaker.Revision > before, "迁移不再开放的帮助话轮会使旧话轮版本过期");
                int stable = speaker.Revision;
                _ = speaker.Current;
                check(speaker.Revision == stable, "重复读取同一话轮不伪造新版本");
                var visited = new HashSet<string>();
                for (int i = 0; i < speaker.requests.Length * 2; i++)
                {
                    visited.Add(speaker.Current.id);
                    check(fresh.CanPresent(speaker.Current, content), "循环话轮保持当前开放范围 " + i);
                    speaker.SelectNext();
                }
                check(!visited.Contains("squirrel.mat") && visited.Contains("squirrel.sequence"), "新档仅开放指定摄影教学，其他帮助前置条件仍在");
                check(fresh.tasks.Count == 0 && fresh.heardRequests.Count == 0, "选话轮不会自动听取或自动接任务");
                fresh.heardRequests.Add("squirrel.sequence");
                speaker.topicIndex = Array.FindIndex(speaker.requests, r => r.id == "squirrel.sequence");
                check(speaker.Current.id == "squirrel.sequence", "听取过的后期话轮仍可再次选择");
                foreach (string id in new[] { "fox.home", "sheep.warm", "fox.warm" })
                {
                    LifeRequest request = Find(id); var early = new LifeProgress();
                    speaker.requests = content.Where(r => r.speakerKey == request.speakerKey).ToArray();
                    speaker.ConfigureProgress(early, content);
                    speaker.topicIndex = Array.FindIndex(speaker.requests, r => r.id == id);
                    check(speaker.Current.id == id && early.tasks.Count == 0 && early.heardRequests.Count == 0,
                        "新档实际话轮选择保留摄影公开语境且不自动接受帮助：" + id);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
            string directory = Path.GetFullPath("LifeGameplayReview/20261026");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "progression-checks.json"), JsonUtility.ToJson(new Result {
                status = "PASS", checks = passed.ToArray(), boundary = "隔离状态与话轮选择检查；不替代真实AI和完整玩家流程。" }, true));
            Debug.Log("Life progression PASS: " + passed.Count);
        }

        static void CompleteEvent(LifeProgress progress, LifeRequest[] content, string eventId)
        {
            // Fixture only: real progress is written by explicit listening and nearby world completion.
            foreach (LifeRequest request in content.Where(r => r.eventId == eventId && r.requiredForEvent))
            {
                if (!progress.heardRequests.Contains(request.id)) progress.heardRequests.Add(request.id);
                if (request.condition != "conversation")
                    progress.tasks.Add(new LifeTask { requestId = request.id, eventId = eventId, status = LifeTaskStatus.Complete });
            }
        }
    }
}
