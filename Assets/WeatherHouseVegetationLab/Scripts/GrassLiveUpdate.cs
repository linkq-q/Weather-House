using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Vegetation
{
    public partial class GrassMeadow
    {
        const int LiveChunkSize = 128;
        const float LiveRestartInterval = .12f;
        const float LiveFrameBudget = .003f;
        public bool IsLiveUpdating { get { return liveWork != null || RequestedRevision != CompletedRevision; } }
        public float LiveProgress { get; private set; } = 1;
        public string LiveError { get; private set; }
        public int RequestedRevision { get; private set; }
        public int CompletedRevision { get; private set; }

        IEnumerator liveWork;
        Transform liveRoot;
        List<LiveChunk>[] liveChunks;
        ShapeState[] observedShapes, appliedShapes;
        LayoutState observedLayout;
        LayoutState[] appliedLayouts;
        bool[] liveDirty;
        Color[] observedRoots, observedTips;
        Vector3 observedAppearance;
        float lastLiveStart = float.NegativeInfinity;
        int buildingRevision;
        bool liveMadeProgress;
        int livePendingKinds, liveKindCursor;
        int[] liveChunkCursor;
        bool liveConfigurationBlocked;

        sealed class LiveChunk
        {
            public MeshFilter filter;
            public int count;
        }

        struct ShapeState : IEquatable<ShapeState>
        {
            GrassKind kind;
            float height, width, tip, spread, root, bend, variation, flowerRadius;
            float headLength, leafAspect, curl, outerBladeFraction, clumpLayering;
            int blades, seed, flowers, petals;
            Color flower, center;
            public ShapeState(GrassSpecies s)
            {
                kind=s.kind; height=s.height; width=s.width; tip=s.tipWidthRatio; spread=s.spread;
                root=s.rootSpread; bend=s.bend; variation=s.heightVariation; flowerRadius=s.flowerRadius;
                blades=s.bladeCount; seed=s.seed; flowers=s.flowerCount; flower=s.flowerColor; center=s.centerColor;
                petals=s.petalCount; headLength=s.headLength; leafAspect=s.leafAspect; curl=s.curl;
                outerBladeFraction=s.outerBladeFraction; clumpLayering=s.clumpLayering;
            }
            public bool Equals(ShapeState s)
            {
                return kind==s.kind && height==s.height && width==s.width && tip==s.tip && spread==s.spread &&
                    root==s.root && bend==s.bend && variation==s.variation && flowerRadius==s.flowerRadius &&
                    blades==s.blades && seed==s.seed && flowers==s.flowers && flower==s.flower && center==s.center &&
                    petals==s.petals && headLength==s.headLength && leafAspect==s.leafAspect && curl==s.curl &&
                    outerBladeFraction==s.outerBladeFraction && clumpLayering==s.clumpLayering;
            }
        }

        struct LayoutState : IEquatable<LayoutState>
        {
            public int[] counts;
            public int seed;
            Vector2 size, pond;
            float radius, water;
            public LayoutState(GrassMeadow meadow, int[] population)
            {
                counts=(int[])population.Clone(); seed=meadow.seed; size=meadow.size;
                pond=meadow.pondCenter; radius=meadow.pondRadius; water=meadow.waterLevel;
            }
            public bool Equals(LayoutState s)
            {
                if (counts==null || s.counts==null || counts.Length!=s.counts.Length || seed!=s.seed || size!=s.size || pond!=s.pond || radius!=s.radius || water!=s.water) return false;
                for (int i=0;i<counts.Length;i++) if (counts[i]!=s.counts[i]) return false;
                return true;
            }
        }

        void ResetLiveTracking()
        {
            liveChunks=null; liveRoot=null;
            if (ConfigurationError!=null) return;
            int length=SpeciesCount;
            if (counts==null || counts.Length!=length) Array.Resize(ref counts,length);
            observedShapes=new ShapeState[length]; appliedShapes=new ShapeState[length];
            observedRoots=new Color[length]; observedTips=new Color[length];
            liveChunkCursor=new int[length]; liveKindCursor=0;
            for (int i=0;i<length;i++)
            {
                observedShapes[i]=appliedShapes[i]=new ShapeState(species[i]);
                observedRoots[i]=species[i].baseColor; observedTips[i]=species[i].tipColor;
            }
            observedAppearance=new Vector3(windStrength,windSpeed,upNormals);
            var applied=new LayoutState(this,counts);
            appliedLayouts=new LayoutState[length];
            for (int i=0;i<length;i++) appliedLayouts[i]=applied;
            liveDirty=new bool[length];
            observedLayout=new LayoutState(this,Allocate(Mathf.Clamp(totalClumps,0,40000),weights));
            CompletedRevision=RequestedRevision;
            LiveProgress=1; LiveError=null; liveConfigurationBlocked=false;
            if (Application.isPlaying && !observedLayout.Equals(applied)) RequestLiveUpdate();
        }

        public void RequestLiveUpdate()
        {
            if (!Application.isPlaying) return;
            RequestedRevision++;
            LiveError=null;
        }

        void Update()
        {
            if (!Application.isPlaying || observedShapes==null) return;
            string configurationError=ConfigurationError;
            if (configurationError!=null)
            {
                CancelLiveBuild(); LiveError=configurationError;
                CompletedRevision=RequestedRevision; liveConfigurationBlocked=true;
                return;
            }
            if (liveConfigurationBlocked) { liveConfigurationBlocked=false; RequestLiveUpdate(); }
            ObserveLiveChanges();
            if (!IsLiveUpdating) return;
            float now=Time.realtimeSinceStartup;
            try
            {
                if (liveWork==null || (liveMadeProgress && livePendingKinds==0 && RequestedRevision!=buildingRevision && now-lastLiveStart>=LiveRestartInterval))
                {
                    CancelLiveBuild();
                    ValidateLiveSettings();
                    EnsureLiveChunks();
                    buildingRevision=RequestedRevision;
                    lastLiveStart=now;
                    LiveProgress=0;
                    liveMadeProgress=false;
                    liveWork=BuildLive();
                }
                float deadline=Time.realtimeSinceStartup+LiveFrameBudget;
                // Never hide the current field while its replacement is being built.
                for (int step=0;step<4;step++)
                {
                    if (!liveWork.MoveNext())
                    {
                        CancelLiveBuild();
                        CompletedRevision=buildingRevision;
                        LiveProgress=1;
                        break;
                    }
                    if (Time.realtimeSinceStartup>=deadline) break;
                }
            }
            catch (Exception error)
            {
                CancelLiveBuild();
                LiveError=error.Message;
                CompletedRevision=RequestedRevision;
                Debug.LogError("草地实时更新失败："+error.Message,this);
            }
        }

        void ObserveLiveChanges()
        {
            var layout=new LayoutState(this,Allocate(Mathf.Clamp(totalClumps,0,40000),weights));
            bool geometry=!layout.Equals(observedLayout);
            observedLayout=layout;
            bool appearance=observedAppearance!=new Vector3(windStrength,windSpeed,upNormals);
            observedAppearance=new Vector3(windStrength,windSpeed,upNormals);
            for (int i=0;i<SpeciesCount;i++)
            {
                var shape=new ShapeState(species[i]);
                geometry|=!shape.Equals(observedShapes[i]); observedShapes[i]=shape;
                appearance|=observedRoots[i]!=species[i].baseColor || observedTips[i]!=species[i].tipColor;
                observedRoots[i]=species[i].baseColor; observedTips[i]=species[i].tipColor;
            }
            if (appearance) ApplyMaterials();
            if (geometry) RequestLiveUpdate();
        }

        void ValidateLiveSettings()
        {
            string error=ConfigurationError;
            if (error!=null) throw new InvalidOperationException(error);
            if (Array.TrueForAll(observedLayout.counts,n=>n==0)) return;
            bool land=false;
            for (int x=0;x<=20 && !land;x++)
                for (int z=0;z<=20 && !land;z++)
                    land=IsPlantable(new Vector2((x/20f-.5f)*size.x,(z/20f-.5f)*size.y));
            if (!land) throw new InvalidOperationException("当前范围没有可种植陆地；已保留现有草地。");
        }

        void EnsureLiveChunks()
        {
            if (liveChunks!=null && liveRoot) return;
            liveChunks=new List<LiveChunk>[SpeciesCount];
            for (int i=0;i<SpeciesCount;i++) liveChunks[i]=new List<LiveChunk>();
            liveRoot=transform.Find("Generated Grass");
            if (!liveRoot)
            {
                liveRoot=new GameObject("Generated Grass").transform;
                liveRoot.SetParent(transform,false);
                counts=new int[SpeciesCount];
                var empty=new LayoutState(this,counts);
                for (int i=0;i<SpeciesCount;i++) appliedLayouts[i]=empty;
                return;
            }
            int[] found=new int[SpeciesCount];
            foreach (Transform child in liveRoot)
            {
                var filter=child.GetComponent<MeshFilter>();
                var renderer=child.GetComponent<MeshRenderer>();
                if (!filter || !renderer) continue;
                for (int i=0;i<SpeciesCount;i++) if (renderer.sharedMaterial==materials[i])
                {
                    int amount=Mathf.Min(LiveChunkSize,Mathf.Max(0,counts[i]-found[i]));
                    liveChunks[i].Add(new LiveChunk {filter=filter,count=amount}); found[i]+=amount;
                    break;
                }
            }
        }

        IEnumerator BuildLive()
        {
            LayoutState wanted=observedLayout;
            var shapes=(ShapeState[])observedShapes.Clone();
            var snapshots=new GrassSpecies[SpeciesCount];
            var jobs=new IEnumerator[SpeciesCount];
            int active=0,units=0,finished=0;
            livePendingKinds=0;
            try
            {
                for (int i=0;i<SpeciesCount;i++)
                {
                    if (!liveDirty[i] && wanted.Equals(appliedLayouts[i]) && shapes[i].Equals(appliedShapes[i])) continue;
                    // A partial replacement remains dirty even when a slider returns to its original value.
                    liveDirty[i]=true;
                    snapshots[i]=Instantiate(species[i]); snapshots[i].hideFlags=HideFlags.DontSave;
                    int chunks=Mathf.CeilToInt(wanted.counts[i]/(float)LiveChunkSize);
                    units+=Mathf.Max(chunks,liveChunks[i].Count)+(chunks>0?4:0);
                    if (chunks>0 || liveChunks[i].Count>0) livePendingKinds|=1<<i;
                    jobs[i]=BuildLiveSpecies(i,wanted,shapes[i],snapshots[i]); active++;
                }
                while (active>0)
                {
                    int kind=liveKindCursor;
                    liveKindCursor=(liveKindCursor+1)%SpeciesCount;
                    if (jobs[kind]==null) continue;
                    if (jobs[kind].MoveNext())
                    {
                        LiveProgress=++finished/(float)Mathf.Max(1,units);
                        yield return null;
                    }
                    else { (jobs[kind] as IDisposable)?.Dispose(); jobs[kind]=null; active--; }
                }
            }
            finally
            {
                foreach (var job in jobs) (job as IDisposable)?.Dispose();
                foreach (var snapshot in snapshots) if (snapshot) Dispose(snapshot);
            }
        }

        IEnumerator BuildLiveSpecies(int kind,LayoutState wanted,ShapeState shape,GrassSpecies snapshot)
        {
            int chunkCount=Mathf.CeilToInt(wanted.counts[kind]/(float)LiveChunkSize);
            var variants=new Mesh[4];
            var completed=new bool[chunkCount];
            try
            {
                if (chunkCount>0)
                    for (int v=0;v<4;v++)
                    {
                        variants[v]=GrassMeshFactory.Build(snapshot,v);
                        variants[v].name="LiveGrassVariant_"+GetInstanceID()+"_"+kind+"_"+v;
                        variants[v].hideFlags=HideFlags.DontSave;
                        yield return null;
                    }
                // Population edits show added/removed clumps before refreshing existing shapes.
                while (liveChunks[kind].Count>chunkCount)
                {
                    RemoveLiveChunk(kind,liveChunks[kind].Count-1);
                    yield return null;
                }
                while (liveChunks[kind].Count<chunkCount)
                {
                    int index=liveChunks[kind].Count;
                    BuildLiveChunk(kind,index,wanted,variants);
                    completed[index]=true;
                    yield return null;
                }
                int start=chunkCount>0?liveChunkCursor[kind]%chunkCount:0;
                for (int offset=0;offset<chunkCount;offset++)
                {
                    int index=(start+offset)%chunkCount;
                    if (completed[index]) continue;
                    BuildLiveChunk(kind,index,wanted,variants);
                    liveChunkCursor[kind]=(index+1)%chunkCount;
                    yield return null;
                }
                appliedShapes[kind]=shape;
                appliedLayouts[kind]=wanted;
                liveDirty[kind]=false;
            }
            finally { foreach (var variant in variants) if (variant) Dispose(variant); }
        }

        void BuildLiveChunk(int kind,int index,LayoutState wanted,Mesh[] variants)
        {
            int amount=Math.Min(LiveChunkSize,wanted.counts[kind]-index*LiveChunkSize);
            var combine=new CombineInstance[amount];
            var random=PlacementRandom(wanted.seed,kind,index);
            for (int j=0;j<amount;j++) combine[j]=NextInstance(random,variants);
            ReplaceLiveChunk(kind,index,amount,combine);
        }

        void ReplaceLiveChunk(int kind,int index,int amount,CombineInstance[] combine)
        {
            var mesh=new Mesh {name="LiveGrass_"+GetInstanceID()+"_"+kind+"_"+index,indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.DontSave};
            try { mesh.CombineMeshes(combine,true,true); }
            catch { Dispose(mesh); throw; }
            Bounds bounds=mesh.bounds; bounds.Expand(1.4f); mesh.bounds=bounds;
            LiveChunk chunk;
            if (index<liveChunks[kind].Count) chunk=liveChunks[kind][index];
            else
            {
                var child=new GameObject(species[kind].name+" · "+index,typeof(MeshFilter),typeof(MeshRenderer));
                child.transform.SetParent(liveRoot,false);
                var renderer=child.GetComponent<MeshRenderer>();
                renderer.sharedMaterial=materials[kind]; renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=true;
                chunk=new LiveChunk {filter=child.GetComponent<MeshFilter>()}; liveChunks[kind].Add(chunk);
            }
            Mesh previous=chunk.filter.sharedMesh;
            chunk.filter.sharedMesh=mesh;
            runtimeMeshes.Add(mesh);
            if (previous && runtimeMeshes.Remove(previous)) Dispose(previous);
            counts[kind]+=amount-chunk.count; chunk.count=amount;
            liveMadeProgress=true;
            livePendingKinds&=~(1<<kind);
        }

        void RemoveLiveChunk(int kind,int index)
        {
            var chunk=liveChunks[kind][index]; liveChunks[kind].RemoveAt(index);
            counts[kind]-=chunk.count;
            liveMadeProgress=true;
            livePendingKinds&=~(1<<kind);
            if (!chunk.filter) return;
            Mesh previous=chunk.filter.sharedMesh;
            if (previous && runtimeMeshes.Remove(previous)) Dispose(previous);
            chunk.filter.gameObject.SetActive(false); Dispose(chunk.filter.gameObject);
        }

        void CancelLiveBuild()
        {
            (liveWork as IDisposable)?.Dispose(); liveWork=null;
        }
        void OnDisable() { CancelLiveBuild(); }
    }
}
