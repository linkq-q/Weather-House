using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Rendering;

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static readonly object lockObject = new object();
    private static bool isApplicationQuitting = false;
    private static T instance;

    public static T Instance
    {
        get
        {
            if (isApplicationQuitting)
            {
                return null;
            }

            lock (lockObject)
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<T>();

                    if (instance == null)
                    {
                        GameObject singletonObject = new($"{typeof(T).Name} (Singleton)");
                        singletonObject.AddComponent<T>();
                    }
                }
            }

            return instance;
        }
    }

    public static bool IsInitialized
    {
        get { return instance != null; }
    }

    protected virtual void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this as T;
    }

    protected virtual void OnDestroy()
    {

    }

    void OnApplicationQuit()
    {
        isApplicationQuitting = true;
    }

    public void Initialize()
    {

    }
}