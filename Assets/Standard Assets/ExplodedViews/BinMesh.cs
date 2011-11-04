using UnityEngine;
using UnityEditor;
using System.Collections;
using System.IO;
using System.Collections.Generic;

// Utilities for dealing with bin clouds (authoring tool)
// - shuffle bin
// - update the minimal mesh (smallest level of detail)
public class BinMesh : MonoBehaviour
{
	// Bin file name (without extension), will be looked for in the usual places.
	public string bin;
	// How many points in the minimal mesh.
	public int minMeshSize = 1000;
	
	public Material material = null;
	public ImportedCloud importedCloud = null;
	
	CloudStream.Reader binReader;
	
	int pointCount = 0;
	public long PointsLeft {
		get {
			return (binReader.BaseStream.Length - binReader.BaseStream.Position) / CloudStream.pointRecSize;
		}
	}
	
	// distance -> LOD
	public float[] lodBreakDistances = new float[] { 100, 15, 7};
	
	public float distanceFromCamera = 0;
	public int lod = 0;
	
	public int DetailMeshCount {
		get {
			return transform.FindChild("Detail").childCount;
		}
	}
	
	public void Start()
	{
		string fname = CloudStream.FindBin(bin + ".bin");
		binReader = new CloudStream.Reader(new FileStream(fname, FileMode.Open, FileAccess.Read));
		
		Transform minmesh = transform.FindChild ("MinMesh");
		if (binReader.BaseStream.Length < CloudMeshPool.pointsPerMesh) {
			minmesh.renderer.sharedMaterial = material;
		} else {
			minmesh.gameObject.active = false;
		}
		
		FileInfo fi = new FileInfo(fname);
		pointCount = (int)fi.Length / CloudStream.pointRecSize;
		
		/*
		 * Clone the shader object, so that we can change it's maxiumLOD 
		 * independently of other materials using the same shader
		 */
		if (material)
			material.shader = Object.Instantiate( material.shader ) as Shader;
		
		material.SetFloat("_TunnelD", lodBreakDistances[lodBreakDistances.Length-1]);
	}
	
	void OnApplicationQuit()
	{
		binReader.Close();
	}
	
	// assume that we've got distance from camera set by LodManager
	public void UpdateLod()
	{
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
	
	[ContextMenu("Generate material")]
	public void GenerateMaterial()
	{
		Shader shader = Shader.Find("Exploded Views/Trilling Opaque Point");
		if (shader) {
			material = new Material(shader);
			material.name = name;
		} else {
			Debug.LogError("Didn't find shader");
		}
	}
	
	[ContextMenu("Shuffle the bin")]
	public void Shuffle()
	{
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
	}
	
	/// <summary>
	/// Actual shuffle implementation
	/// </summary>
	/// <param name="stream">
	/// A <see cref="FileStream"/> of the cloud bin.
	/// </param>
	/// <param name="prog">
	/// A <see cref="Progressor"/> for progress indication. Supplied by the caller to 
	/// accomodate integration into longer processes.
	/// </param>
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
	
	[ContextMenu("Refresh Minimal Mesh")]
	public void RefreshMinMesh()
	{
		Transform minMeshNode = transform.FindChild("MinMesh");
		if (!minMeshNode)
			throw new Pretty.AssertionFailed("Need MinMesh child");
		MeshFilter minMeshFilter = minMeshNode.GetComponent<MeshFilter>();
		if (!minMeshFilter)
			throw new Pretty.AssertionFailed("Need MinMesh child");
		
		CloudStream.Reader reader = new CloudStream.Reader(new FileStream(CloudStream.FindBin(bin + ".bin"), 
				FileMode.Open));
		
		try {
			CloudMeshConvertor conv = new CloudMeshConvertor(minMeshSize);
			Mesh mesh = conv.MakeMesh();
			reader.ReadPoints(conv.vBuffer, conv.cBuffer);
			conv.Convert(mesh);
			minMeshFilter.mesh = mesh;
			mesh.name = bin;
		} finally {
		
			reader.Close();
		}
	}
	
	[ContextMenu("Generate collider box")]
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

	public IEnumerator LoadOne(GameObject go)
	{
		Transform detailBranch = transform.FindChild("Detail");
		yield return StartCoroutine(CloudMeshPool.ReadFrom(binReader, go));
		ProceduralUtils.InsertHere(go.transform, detailBranch);
		go.active = true;
		if (material)
			go.renderer.sharedMaterial = material;
		
	}
	
	public void ReturnDetails(int count)
	{
		Transform detail = transform.FindChild("Detail");
		for(int i = 0; i < count && detail.childCount > 0; i++) {
			// remove the last child
			CloudMeshPool.Return(detail.GetChild(detail.childCount - 1).gameObject);
			// rewind the reader
			binReader.SeekPoint(-16128, SeekOrigin.Current);
		}
	}
	
	#region Photocams
	
	[ContextMenu("Generate Photo-Camera triggers")]
	public void GenerateCamTriggers ()
	{
		if (!importedCloud) {
			throw new Pretty.Exception ("No link to original imported cloud in {0}", name);
		}
		// find cams.txt
		string cams_fname = string.Format ("cams/{0}/cams.txt", Path.GetFileNameWithoutExtension (importedCloud.binPath));
		Debug.Log (string.Format ("Will look for cams in {0}", cams_fname));
		TextReader tr;
		try {
			tr = new StreamReader (cams_fname);
		} catch (DirectoryNotFoundException) {
			Debug.LogWarning ("cam.txt not found :(");
			return;
		}
		
		Transform cams_go = transform.Find ("CamTriggers");
		if (cams_go != null) {
			DestroyImmediate (cams_go.gameObject);
			cams_go = null;
		}
		
		try {
			Transform cameraStandIn = AssetDatabase.LoadAssetAtPath ("Assets/Prefabs/CamTriggerTemplate.prefab", 
				typeof(Transform)) as Transform;
		
			string line;
			while ((line = tr.ReadLine ()) != null) {
				string[] words = line.Split (' ');
				// find corresponding slice in importedCloud
				ImportedCloud.Slice foundSlice = null;
				foreach (ImportedCloud.Slice slice in importedCloud.slices) {
					if (slice.name.Contains (words[0])) {
						if (foundSlice == null)
							foundSlice = slice;
						else
							throw new Pretty.Exception ("Ambiguous cam name: {0}, matches slices {1} and {2}", 
								words[0], foundSlice.name, slice.name);
					}
				}
				if (foundSlice != null) {
					// do the do
					if (foundSlice.selected) {
						// only add a CamTriggers node if have something to add to it
						if (cams_go == null) {
							cams_go = new GameObject ("CamTriggers").transform;
							ProceduralUtils.InsertHere (cams_go, transform);
						}
						
						Transform trigger = Object.Instantiate (cameraStandIn) as Transform;
						trigger.name = words[0];
						ProceduralUtils.InsertHere (trigger, cams_go);
						trigger.localPosition = new Vector3 (float.Parse (words[1]), -float.Parse (words[2]), float.Parse (words[3]));
						
						Transform ps = trigger.Find ("Particle System");
						// force is in world coordinates
						ps.GetComponent<ParticleAnimator> ().force = ps.TransformDirection (
							new Vector3 (float.Parse (words[4]), -float.Parse (words[5]), float.Parse (words[6])));
					}
				} else {
					Debug.LogWarning (string.Format ("Can't find camera {0} in ImportedCloud {1}", 
							words[0], importedCloud.name));
				}
			}
		} finally {
			tr.Close();
		}
	}
	#endregion
	
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
}
