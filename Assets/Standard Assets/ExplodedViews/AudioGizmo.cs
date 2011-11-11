using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]
[AddComponentMenu("Exploded Views/Sound Gizmos")]
public class AudioGizmo : MonoBehaviour
{
	void OnDrawGizmos()
	{
		Gizmos.color = new Color(0,1,.5f,0.25f);
		Gizmos.DrawSphere( transform.position, audio.minDistance );
		Gizmos.DrawSphere( transform.position, audio.maxDistance );
	}
}
