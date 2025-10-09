using UnityEngine;

public class MouseMovement : MonoBehaviour
{
    private Vector3 mousePosition;
    //public GameObject pointer;
    public float speed = 5f; // Adjust the speed of movement
    public float rotationSpeed = 10f; // degrees per second

    void Update()
    {
        // Get mouse position in screen coordinates
        mousePosition = Input.mousePosition;

        // Convert screen position to world position
        Vector3 worldPosition = Camera.main.ScreenToWorldPoint(new Vector3(mousePosition.x, mousePosition.y, transform.position.z));

        // Move the object towards the world position
        transform.position = new Vector3(mousePosition.x / 1000, mousePosition.y / 1000, transform.position.z);
        //Debug.Log($"Mouse Position: {mousePosition}, World Position: {worldPosition}");

        // key driven z movement g h
        float moveZ = 0f;
        if (Input.GetKey(KeyCode.G)) moveZ += 0.1f * Time.deltaTime; // Move forward
        if (Input.GetKey(KeyCode.H)) moveZ -= 0.1f * Time.deltaTime; // Move backward
        transform.Translate(0, 0, moveZ, Space.World);


        // --- keyboard‐driven rotation ---
        float yawInput = Input.GetAxis("Horizontal"); // A/D or ←/→ → Y‐axis
        float pitchInput = Input.GetAxis("Vertical");   // W/S or ↑/↓ → X‐axis

        // Roll with Q/E keys (no default Input.GetAxis, so we do it manually)
        float rollInput = 0f;
        if (Input.GetKey(KeyCode.Q)) rollInput += 1f;
        if (Input.GetKey(KeyCode.E)) rollInput -= 1f;

        // Compute angle deltas
        float yawDelta = yawInput * rotationSpeed * Time.deltaTime;
        float pitchDelta = pitchInput * rotationSpeed * Time.deltaTime;
        float rollDelta = rollInput * rotationSpeed * Time.deltaTime;

        // Apply them in local space:
        // pitch = rotate about local X
        // yaw   = rotate about local Y
        // roll  = rotate about local Z
        transform.Rotate(pitchDelta, yawDelta, rollDelta, Space.World);
    }
}