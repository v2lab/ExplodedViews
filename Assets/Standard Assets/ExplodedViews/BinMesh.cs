using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;

// Utilities for dealing with bin clouds (authoring tool)
// - shuffle bin
// - update the minimal mesh (smallest level of detail)
public class BinMesh : Inflatable
{
	// Bin file name (without extension), will be looked for in the usual places.
	public string bin;
	// How many points in the minimal mesh.
	public int minMeshSize = 1000;
	public Material material = null;
	CloudStream.Reader binReader = null;
	int pointCount = 0;

	Transform mainCameraTransform;
	//LodManager lodManager = null;
	Collider box;
	
	// distance -> LOD
	public float[] lodBreakDistances = new float[] { 100, 15, 7};
	public float distanceFromCamera = 0;
	public int lod = 0;

	public override string BinPath { get { return ExplodedPrefs.BoxBin(name); } }


	public float Scale {
		get { return scale; }
		set {
			scale = value;
			// scale min mesh
			Mesh minMesh = transform.FindChild("MinMesh").GetComponent<MeshFilter>().mesh;
			Vector3[] v = minMesh.vertices;
			for(int i = 0; i<v.Length; ++i) {
				v[i] = v[i] * scale;
			}
			minMesh.vertices = v;
			minMesh.RecalculateBounds();
			// scale the box
			Transform box = transform.FindChild("Box");
			box.localPosition = box.localPosition * scale;
			box.localScale = box.localScale * scale;
		}
	}

	public long PointsLeft {
		get {
			return (binReader.BaseStream.Length - binReader.BaseStream.Position) / CloudStream.pointRecSize;
		}
	}
	
	public override void Awake()
	{
		mainCameraTransform = GameObject.Find("Camera").transform;
		//lodManager = GameObject.Find("LodManager").GetComponent<LodManager>();
		box = transform.FindChild("Box").collider;
		base.Awake();
	}

	public void Start()
	{
		throw new System.NotSupportedException("Port this to Prefs");
#if NEVER
		if (CloudStream.FindBin(bin + ".bin")==null) {
			Object.DestroyImmediate( this );
			return;
		}

		if (lodManager && lodManager.forcedBinMeshMaterial!=null) {
			material  = lodManager.forcedBinMeshMaterial;
		}

		/*
		 * Clone the shader object, so that we can change it's maxiumLOD 
		 * independently of other materials using the same shader.
		 *
		 * Since we started to save material in prefab, we need to clone
		 * material first, otherwise it loses the shader
		 */
		if (material) {
			material = Object.Instantiate( material ) as Material;
			material.shader = Object.Instantiate( material.shader ) as Shader;
		}

		{
			Managed = true;
			Transform minmesh = transform.FindChild ("MinMesh");
			minmesh.renderer.material = material;
			pointCount = (int)binReader.BaseStream.Length / CloudStream.pointRecSize - minMeshSize;
			Managed = false;
		}

		if (lodManager && lodManager.overrideLodBreaks) {
			lodBreakDistances = lodManager.lodBreakDistances;
		}

		material.SetFloat("_TunnelD", lodBreakDistances[lodBreakDistances.Length-1]);

		enabled = false; // will get enabled by lod manager
#endif
	}

	public override bool Managed {
	set {
			if (base.Managed = value && binReader == null) {
				// FIXME: should use open/close and not new to avoid garbage generation
				throw new System.NotImplementedException("Port this to Prefs");
				/*
				//binReader = new CloudStream.Reader(new FileStream(CloudStream.FindBin(bin + ".bin"), FileMode.Open, FileAccess.Read));
				// skip the minmeshsized chunk
				binReader.SeekPoint(minMeshSize);
				enabled = true;
				*/
			} else if (binReader != null) {
				binReader.Close();
				binReader = null;
				Entitled = 0;
				enabled = false;
			}
		}
	}

	public void Update()
	{
		// update distance to camera
		Vector3 closestPoint = box.ClosestPointOnBounds(mainCameraTransform.position);
		distanceFromCamera = Vector3.Distance(mainCameraTransform.position, closestPoint);
		
		int i;
		for(i = 0; i < lodBreakDistances.Length && distanceFromCamera < lodBreakDistances[i]; i++)
			;
		
		float lod_t = 0;
		if (i > 0) {
			lod_t = 
				(float)(i - 1) 
					+ Mathf.InverseLerp(
						lodBreakDistances[i - 1], 
						(i < lodBreakDistances.Length) ? lodBreakDistances[i] : 0, 
						distanceFromCamera);
		}
		
		int old_lod = lod;
		lod = Mathf.FloorToInt(lod_t * 250);
		
		const int turbAt = 250;
		
		if (old_lod < turbAt && lod >= turbAt) {
			animation.Play("Turbulator");
		} else if (old_lod >= turbAt && lod < turbAt) {
			// first jump to start...
			animation.Rewind("Turbulator");
			// ... and update shader parameter (for the future)
			animation.Sample();
			// then stop to prevent overwriting shader parameters
			animation.Stop("Turbulator");
		}
		
		material.shader.maximumLOD = lod;
		material.SetFloat("_SubLod", distanceFromCamera > 0 ? lod_t - Mathf.Floor(lod_t) : 1);
		UpdateMaterial();
	}


	public override int NextChunkSize
	{
		get {
			return CloudMeshPool.pointsPerMesh;
		}
	}


	public override void PreLoad(GameObject go)
	{
		// nothing to do
	}
	public override void PostLoad(GameObject go)
	{
		if (material)
			go.renderer.sharedMaterial = material;
	}
	public override void PostUnload()
	{
		if (binReader == null || binReader.PointPosition == 0)
			return;
		
		int tail = (int)(binReader.PointPosition % CloudMeshPool.pointsPerMesh);
		if (tail == 0)
			tail = CloudMeshPool.pointsPerMesh;
		
		binReader.SeekPoint(-tail , SeekOrigin.Current);
	}


	#region Material Animation
	public float turbulenceAmplitude = 0;
	float _lastTurbulenceAmplitude = -1;
	
	public float turbulenceFrequency = 5;
	float _lastTurbulenceFrequency = -1;
	
	public float turbulenceCurliness = 0;
	float _lastTurbulenceCurliness = -1;
	
	void UpdateMaterialProperty(float value, ref float oldValue, string property)
	{
		if (value != oldValue) {
			material.SetFloat(property, value);
			oldValue = value;
		}
	}
	
	void UpdateMaterial()
	{
		if (!material)
			return;
		
		UpdateMaterialProperty(turbulenceAmplitude, ref _lastTurbulenceAmplitude, "_TurbulenceAmplitude");
		UpdateMaterialProperty(turbulenceFrequency, ref _lastTurbulenceFrequency, "_TurbulenceFrequency");
		UpdateMaterialProperty(turbulenceCurliness, ref _lastTurbulenceCurliness, "_TurbulenceCurliness");
	}
	#endregion

#if UNITY_EDITOR
	public void GenerateMaterial()
	{
		Shader shader = Shader.Find("Exploded Views/Trilling Opaque Point");
		if (shader) {
			Debug.Log("Updating the material for " + name);
			material = new Material(shader);
			material.name = name;
		} else {
			Debug.LogError("Didn't find shader");
		}
	}
	
	public void Shuffle()
	{
		throw new System.NotImplementedException("Prot this to Prefs");
		/*
		string fname = CloudStream.FindBin(bin + ".bin");
		FileInfo fi = new FileInfo(fname);
		pointCount = (int) fi.Length / CloudStream.pointRecSize;
		FileStream stream = new FileStream(fname, FileMode.Open);
		Progressor progressor = new Progressor(string.Format("Shuffling {0}", bin));
		try {
			Shuffle(stream, progressor);
		} finally {
			stream.Close();
			progressor.Done("Shuffling {0} points took {tt} ({1} points per sec)", 
				Pretty.Count(pointCount), Pretty.Count((float)pointCount / progressor.elapsed));
			RefreshMinMesh();
		}
		*/
	}
	
	public void Shuffle(FileStream stream, Progressor prog)
	{
		int byteCount = pointCount * CloudStream.pointRecSize;
		byte[] wholeThing = new byte[byteCount];
		
		Progressor subProg = prog.Sub(0f, 0.1f);
		subProg.Progress(0.01f, "Reading into memory...");
		stream.Seek(0, SeekOrigin.Begin);
		stream.Read( wholeThing, 0, byteCount );
		subProg.Progress(1f, "Reading into memory... done");
				
		byte[] tmp = new byte[CloudStream.pointRecSize];
		
		subProg = prog.Sub(0.1f, 0.9f);
		ShuffleUtility.WithSwap(pointCount, (i, j) =>
		{
			/*
             * This is the fastest way I found to swap 16-byte long chunks in memory (tried MemoryStream and
             * byte-by-byte swap loop).
             */
			System.Buffer.BlockCopy(wholeThing, i * CloudStream.pointRecSize, tmp, 0, CloudStream.pointRecSize);
			System.Buffer.BlockCopy(wholeThing, j * CloudStream.pointRecSize, wholeThing, i * CloudStream.pointRecSize, CloudStream.pointRecSize);
			System.Buffer.BlockCopy(tmp, 0, wholeThing, j * CloudStream.pointRecSize, CloudStream.pointRecSize);
			// 'i' runs backwards from pointCount-1 to 0
			subProg.Progress((float)(pointCount - i) / pointCount, "Shuffling '{0}' in memory. ETA: {eta}", name);
		});
		
		subProg = prog.Sub(0.9f, 1f);
		subProg.Progress(0.01f, "Writing to disk...");
		stream.Seek(0, SeekOrigin.Begin);
		stream.Write( wholeThing, 0, byteCount );
		subProg.Progress(1f, "Writing to disk... done");
	}
	
	public Mesh RefreshMinMesh() {
		return RefreshMinMesh(null);
	}

	public Mesh RefreshMinMesh(Mesh mesh)
	{
		Transform minMeshNode = transform.FindChild("MinMesh");
		if (!minMeshNode)
			throw new Pretty.AssertionFailed("Need MinMesh child");
		MeshFilter minMeshFilter = minMeshNode.GetComponent<MeshFilter>();
		if (!minMeshFilter)
			throw new Pretty.AssertionFailed("Need MinMesh child");

		CloudStream.Reader reader = null;
		try {
			throw new System.NotImplementedException("Port this to Prefs");
			/*
			reader = new CloudStream.Reader(new FileStream(CloudStream.FindBin(bin + ".bin"), FileMode.Open));
			CloudMeshConvertor conv = new CloudMeshConvertor(minMeshSize);
			if (mesh == null)
				mesh = conv.MakeMesh();
			reader.ReadPoints(conv.vBuffer, conv.cBuffer);
			conv.Convert(mesh);
			minMeshFilter.mesh = mesh;
			mesh.name = name + "-miniMesh";
			return mesh;
			*/
		} catch {
			return null;
		} finally {
			if (reader != null)
				reader.Close();
		}
	}
	
	public void GenerateColliderBox ()
	{
		if (transform.Find("Box")!=null) {
			Object.DestroyImmediate(transform.Find ("Box").gameObject);
		}
		Transform minMesh = transform.FindChild ("MinMesh");
		Bounds bounds = minMesh.renderer.bounds;
		Transform node = new GameObject("Box", typeof(BoxCollider)).transform;
		node.position = bounds.center;
		node.localScale = bounds.size;
		node.parent = transform;
	}
#endif
}

