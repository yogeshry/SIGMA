using System;
using System.Collections.Concurrent;
using UnityEngine;

public class MainThreadDispatcher : MonoBehaviour
{
    private static MainThreadDispatcher instance;
    private static readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

    /// <summary>
    /// Singleton instance of the dispatcher. Automatically creates an instance if needed.
    /// </summary>
    public static MainThreadDispatcher Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject dispatcherObject = new GameObject("MainThreadDispatcher");
                instance = dispatcherObject.AddComponent<MainThreadDispatcher>();
                DontDestroyOnLoad(dispatcherObject);
            }
            return instance;
        }
    }

    /// <summary>
    /// Initializes the dispatcher before the first scene loads.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeDispatcher()
    {
        // Force the Instance property to initialize the dispatcher.
        var _ = Instance;
    }

    /// <summary>
    /// Enqueues an action to be executed on the main thread.
    /// </summary>
    public static void Enqueue(Action action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        mainThreadQueue.Enqueue(action);
    }

    /// <summary>
    /// Processes all queued actions on the main thread.
    /// </summary>
    public static void ProcessMainThreadQueue()
    {
        while (mainThreadQueue.TryDequeue(out Action action))
        {
            action?.Invoke();
        }
    }

    // Process queued actions every frame.
    private void Update()
    {
        ProcessMainThreadQueue();
    }
}
