using UnityEngine;
using WebSocketSharp.Server;

public class BroadcastBehaviour : WebSocketBehavior
{
    // store the sessions for this service so we can broadcast later
    public static WebSocketSessionManager SessionsManager { get; private set; }

    protected override void OnOpen()
    {
        // cache the sessions when the first client connects
        SessionsManager = Sessions;
    }

    public void BroadcastPose(string poseJson)
    {
        Sessions.Broadcast(poseJson);
    }

    // static helper that other classes can call
    public static void BroadcastGlobal(string poseJson)
    {
        if (SessionsManager == null)
        {
            Debug.Log("RelativePoseBehaviour: no sessions available to broadcast.");
            return;
        }
        SessionsManager.Broadcast(poseJson);
    }
}
