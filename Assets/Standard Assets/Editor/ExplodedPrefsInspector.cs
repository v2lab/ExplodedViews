using UnityEditor;
using UnityEngine;
using System.Collections;
using System.IO;

[CustomEditor(typeof(ExplodedPrefs))]
public class ExplodedPrefsInspector : Editor
{
	ExplodedPrefs prefs { get { return target as ExplodedPrefs; } }

	public override void OnInspectorGUI()
	{
		pathButton("Incoming Dir", ref prefs.incomingPath);
		pathButton("Imported Dir", ref prefs.importedPath);
	}

	void pathButton(string label, ref string path)
	{
		GUILayout.Label( label );
		if (GUILayout.Button( path )) {
			string res = EditorUtility.OpenFolderPanel("Select " + label, path, "");
			if (res != null && res != "") {
				path = res;
				EditorUtility.SetDirty(prefs);
			}

		}
	}

	[MenuItem("Exploded Views/Prefs Asset")]
	static void CreateResource()
	{
		if (EditorUtility.DisplayDialog("Create Exploded Prefs",
		                                "Existing preferences will be overwritten. Are you sure?\n" +
		                                "Preferences asset can be found in Project Window > Resources > ExplodedPrefs",
		                                "Yes",
		                                "No")) {
			string res = Path.Combine("Assets","Resources");
			if (!Directory.Exists(res))
				Directory.CreateDirectory(res);
	
			ExplodedPrefs prefs = ScriptableObject.CreateInstance<ExplodedPrefs>();
			prefs.importedPath = Path.GetFullPath("Bin");
			AssetDatabase.CreateAsset(prefs, Path.Combine(res, "ExplodedPrefs.asset"));
			AssetDatabase.Refresh();
		}
	}

}

