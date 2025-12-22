// BillboardFaceCamera.cs
using UnityEngine;

public class BillboardFaceCamera : MonoBehaviour
{
    public enum FaceMode { CameraOrientation, CameraPosition }
    public FaceMode mode = FaceMode.CameraOrientation;
    public bool onlyY = true;     // rotate around Y only
    public float minSqr = 1e-8f;  // avoid NaNs when camera is too close

    Camera _cam;

    void OnEnable() { FindCam(); FaceNow(); }
    void LateUpdate() { if (!_cam) FindCam(); FaceNow(); }

    void FindCam()
    {
        _cam = Camera.main;
        if (!_cam)
        {
            // Fallbacks: first enabled camera in scene
            var cams = Object.FindObjectsOfType<Camera>();
            if (cams.Length > 0) _cam = cams[0];
        }
    }

    public void FaceNow()
    {
        if (!_cam) return;

        if (mode == FaceMode.CameraOrientation)
        {
            if (onlyY)
            {
                // Project camera forward to XZ and face that direction
                var fwd = _cam.transform.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < minSqr) fwd = (transform.position - _cam.transform.position); // fallback
                transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            }
            else
            {
                transform.LookAt(
                    transform.position + _cam.transform.rotation * Vector3.forward,
                    _cam.transform.rotation * Vector3.up
                );
            }
        }
        else // FaceMode.CameraPosition
        {
            if (onlyY)
            {
                var dir = _cam.transform.position - transform.position; dir.y = 0f;
                if (dir.sqrMagnitude < minSqr) return;
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up) *
            Quaternion.Euler(0f, 180f, 0f);
            }
            else
            {
                transform.rotation = Quaternion.LookRotation(
                    _cam.transform.position - transform.position,
                    Vector3.up
                );
            }
        }
    }
}
