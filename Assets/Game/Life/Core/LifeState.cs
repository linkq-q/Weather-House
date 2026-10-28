using System;
using System.Collections.Generic;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    [Serializable]
    public sealed class LifeRequest
    {
        public string id, eventId, speakerKey, listenerKey, meaning, objective, condition, targetId, context;
        public string[] glyphIds = Array.Empty<string>();
        public bool requiredForEvent;
        public bool openForPhotography;
        public bool HasTask => !string.IsNullOrWhiteSpace(objective) && condition != "conversation";
    }

    [Serializable]
    public sealed class LifeContextSnapshot
    {
        public string speakerId, listenerId, targetId, sequenceId, objectState, weatherState;
        public string speakerName, listenerName, locationName;
        public Vector3 speakerPosition, listenerPosition, anchorPosition;
        public LifeContextSnapshot Copy() => (LifeContextSnapshot)MemberwiseClone();
    }

    public enum LifeTaskStatus { Active, Complete }

    [Serializable]
    public sealed class LifeTask
    {
        public string requestId, animalId, eventId;
        public LifeTaskStatus status;
        public long acceptedUtcTicks, completedUtcTicks;
    }

    [Serializable]
    public sealed class LifeProgress
    {
        public int version = 1;
        public List<LifeTask> tasks = new List<LifeTask>();
        public List<string> learnedGestures = new List<string>();
        public List<string> friendlyAnimals = new List<string>();
        public List<string> heardRequests = new List<string>();
        // Retained only to migrate existing saves; listening never adds understanding.
        public List<string> understoodRequests = new List<string>();
        public string worldJson = "";
        public bool rainTutorialSeen;

        public void Normalize()
        {
            tasks = tasks ?? new List<LifeTask>();
            learnedGestures = learnedGestures ?? new List<string>();
            friendlyAnimals = friendlyAnimals ?? new List<string>();
            understoodRequests = understoodRequests ?? new List<string>();
            heardRequests = heardRequests ?? new List<string>();
            foreach (string id in understoodRequests) RememberHeard(id);
            foreach (LifeTask task in tasks) if (task != null) RememberHeard(task.requestId);
        }

        public LifeTask FindTask(string requestId)
        {
            foreach (LifeTask task in tasks) if (task != null && task.requestId == requestId) return task;
            return null;
        }

        public bool HasHeard(string requestId) => !string.IsNullOrEmpty(requestId) && heardRequests.Contains(requestId);
        void RememberHeard(string requestId)
        {
            if (!string.IsNullOrEmpty(requestId) && !heardRequests.Contains(requestId)) heardRequests.Add(requestId);
        }

        public bool Hear(LifeRequest request, string animalId, bool requestStillCurrent, bool nearby)
        {
            if (request == null || string.IsNullOrEmpty(request.id) || string.IsNullOrEmpty(animalId) ||
                !requestStillCurrent || !nearby) return false;
            RememberHeard(request.id);
            if (!request.HasTask || FindTask(request.id) != null) return false;
            tasks.Add(new LifeTask { requestId = request.id, animalId = animalId, eventId = request.eventId,
                status = LifeTaskStatus.Active, acceptedUtcTicks = DateTime.UtcNow.Ticks });
            return true;
        }

        public bool Complete(string requestId, bool nearby, bool worldSatisfied)
        {
            LifeTask task = FindTask(requestId);
            if (!nearby || !worldSatisfied || task == null || task.status != LifeTaskStatus.Active) return false;
            task.status = LifeTaskStatus.Complete;
            task.completedUtcTicks = DateTime.UtcNow.Ticks;
            if (!friendlyAnimals.Contains(task.animalId)) friendlyAnimals.Add(task.animalId);
            return true;
        }

        public bool EventComplete(string eventId, IEnumerable<LifeRequest> requests)
        {
            bool hasRequired = false;
            foreach (LifeRequest request in requests)
            {
                if (request == null || request.eventId != eventId || !request.requiredForEvent) continue;
                hasRequired = true;
                bool done = request.condition == "conversation" ? HasHeard(request.id) :
                    FindTask(request.id)?.status == LifeTaskStatus.Complete;
                if (!done) return false;
            }
            return hasRequired;
        }

        public bool CanPresent(LifeRequest request, IEnumerable<LifeRequest> content)
        {
            if (request == null) return false;
            if (request.openForPhotography) return true;
            // Heard requests and accepted help remain available on every return visit.
            if (HasHeard(request.id) || FindTask(request.id) != null) return true;
            if (!request.HasTask && !request.requiredForEvent) return true;
            if (request.id == "dog.rain.lesson") return HasHeard("dog.hello");
            switch (request.eventId)
            {
                case "E0": return true;
                case "E1": return EventComplete("E0", content);
                case "E2": return EventComplete("E1", content);
                case "E3": case "E4": case "E5": case "E6a": case "E6b":
                    return EventComplete("E2", content);
                case "E7":
                    foreach (string id in BeforeEnding) if (!EventComplete(id, content)) return false;
                    return true;
                default: return false;
            }
        }
        private static readonly string[] BeforeEnding = { "E0", "E1", "E2", "E3", "E4", "E5", "E6a", "E6b" };

        public string GetProgressHint(IEnumerable<LifeRequest> content)
        {
            if (!EventComplete("E0", content)) return HasHeard("dog.rain.lesson") ?
                "在它身边试试手账里最初的手势，看看周围的变化。" :
                HasHeard("dog.hello") ? "听听它的下一句话，看看可以怎样帮忙。" :
                "走近院门的动物，先听它说些什么。";
            if (!EventComplete("E1", content)) return "看看雨落下时与停下后，院边的碗有什么不同。";
            if (!EventComplete("E2", content)) return "看看窝垫和旁边的草，比较它们淋湿前后的样子。";
            if (!EventComplete("E3", content)) return "看看动物指向的树，再观察它真正停下来休息的地方。";
            if (!EventComplete("E4", content)) return "站在布条旁，比较天气变化时动物的反应。";
            if (!EventComplete("E5", content)) return "比较明亮的空地和背风处，看看动物愿意在哪里停留。";
            if (!EventComplete("E6a", content)) return "听听渡口的两只动物说话，留意每句话是对谁说的。";
            if (!EventComplete("E6b", content)) return "站在安全干岸，留意说话者指向的位置和移动方向。";
            if (!EventComplete("E7", content)) return "再去河岛，看看两件熟悉物品怎样一起满足歇脚需要。";
            return "可以再去看望动物，也可以继续拍照、记录还没弄懂的字。";
        }
    }

    [DisallowMultipleComponent]
    public sealed class LifeSpeaker : MonoBehaviour
    {
        public string key;
        public AnimalActor Actor;
        public LifeRequest[] requests = Array.Empty<LifeRequest>();
        public int topicIndex;
        public int Revision { get; private set; }
        private LifeProgress progress = new LifeProgress();
        private IEnumerable<LifeRequest> content = Array.Empty<LifeRequest>();
        private LifeRequest lastCurrent;
        public void ConfigureProgress(LifeProgress value, IEnumerable<LifeRequest> allRequests)
        {
            progress = value ?? new LifeProgress();
            content = allRequests ?? Array.Empty<LifeRequest>();
        }
        private bool Available(LifeRequest request) => progress.CanPresent(request, content);
        public LifeRequest Current
        {
            get
            {
                LifeRequest selected = null;
                if (requests != null && requests.Length > 0)
                {
                    topicIndex = Mathf.Clamp(topicIndex, 0, requests.Length - 1);
                    if (!Available(requests[topicIndex]))
                    {
                        int next = Array.FindIndex(requests, r => Available(r) && r.requiredForEvent &&
                            !progress.HasHeard(r.id));
                        if (next < 0) next = Array.FindIndex(requests, Available);
                        if (next >= 0) topicIndex = next;
                    }
                    if (Available(requests[topicIndex])) selected = requests[topicIndex];
                }
                // AnimalActor includes Revision in its utterance ID: migrating a locked topic
                // invalidates a captured interaction just like an explicit topic change.
                if (!ReferenceEquals(lastCurrent, selected)) { lastCurrent = selected; Revision++; }
                return selected;
            }
        }
        public void SelectNext()
        {
            if (requests == null || requests.Length == 0) return;
            _ = Current;
            for (int step = 1; step <= requests.Length; step++)
            {
                int next = (topicIndex + step) % requests.Length;
                if (!Available(requests[next])) continue;
                topicIndex = next;
                _ = Current;
                return;
            }
        }

        public void AdvanceAfterHeard()
        {
            var current = Current;
            if (current == null || !progress.HasHeard(current.id) ||
                (current.HasTask && progress.FindTask(current.id)?.status != LifeTaskStatus.Complete)) return;
            // Prefer the next unfinished authored request, then new observations, then revisit known topics.
            for (int priority = 0; priority < 3; priority++)
                for (int step = 1; step < requests.Length; step++)
                {
                    int next = (topicIndex + step) % requests.Length;
                    var candidate = requests[next];
                    if (!Available(candidate)) continue;
                    if (priority == 0 && (!candidate.requiredForEvent ||
                        (candidate.HasTask ? progress.FindTask(candidate.id)?.status == LifeTaskStatus.Complete : progress.HasHeard(candidate.id)))) continue;
                    if (priority == 1 && progress.HasHeard(candidate.id)) continue;
                    topicIndex = next;
                    _ = Current;
                    return;
                }
        }
    }
}
