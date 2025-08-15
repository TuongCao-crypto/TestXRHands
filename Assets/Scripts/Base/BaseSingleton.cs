
using System;
using System.Collections;
using UnityEngine;

//========================================================
// class BaseSingleton
//========================================================
// - for making singleton object
// - usage
//		+ declare class(derived )	
//			public class OnlyOne : BaseSingleton< OnlyOne >
//		+ client
//			OnlyOne.Instance.[method]
//========================================================
public abstract class Singleton<T> where T : new()
{
	private static T instance;
	public static T Instance
	{
		get
		{
			if (instance == null)
			{
				instance = new T();
			}
			return instance;
		}
	}
}


/// <summary>
/// Singleton for mono behavior object
/// </summary>
/// <typeparam name="T"></typeparam>
public class SingletonMono<T> : MonoBehaviour where T : MonoBehaviour
{
	private static T singleton;
	
	public static bool IsInstanceValid() { return singleton != null; }
	
	void Reset()
	{
		gameObject.name = typeof(T).Name;
	}
	
	public static T Instance
	{
		get
		{
			if (SingletonMono<T>.singleton == null)
			{
				SingletonMono<T>.singleton = (T)FindObjectOfType(typeof(T));
				if (SingletonMono<T>.singleton == null)
				{
					GameObject obj = new GameObject();
					obj.name = typeof(T).Name;
					SingletonMono<T>.singleton = obj.AddComponent<T>();
				}
			}
			
			return SingletonMono<T>.singleton;
		}
	}
	
}

/// <summary>
/// Singleton for mono behavior object
/// </summary>
/// <typeparam name="T"></typeparam>
public class ManualSingletonMono<T> : MonoBehaviour where T : MonoBehaviour
{
	public static T Instance { get; private set; }
	public static bool IsInstanceValid() { return Instance != null; }
	void Reset()
	{
		gameObject.name = typeof(T).Name;
	}
	
	protected virtual void Awake()
	{
		if (Instance == null)
			Instance = (T)(MonoBehaviour)this;
	}
	
	protected virtual void OnDestroy()
	{
		if (Instance == this)
			Instance = null;
	}
}
public class SingletonMonoAwake<T> : MonoBehaviour where T : MonoBehaviour
{
	private static T instance = null;
	public static bool IsInstanceValid() { return instance != null; }
	public static T Instance
	{
		get
		{
			if(instance==null)
			{
				Debug.LogError(typeof(T).ToString() + " is not created yet");
			}
			return instance;
		}
	}
	
	void Awake()
	{
		if(instance==null)
        {
            instance = gameObject.GetComponent<T>();
			DontDestroyOnLoad(gameObject);
			OnAwake();
		}else
		{
			//prevent to create a new gameobject if existed
			GameObject.Destroy(gameObject);
		}
	}
	
	public virtual void OnAwake()
	{
		// override this to do something on Awake method
	}
}

public class SingletonMonoStart<T> : MonoBehaviour where T : MonoBehaviour
{
	private static T instance = null;
	public static bool IsInstanceValid() { return instance != null; }
	public static T Instance
	{
		get
		{
			if (instance == null)
			{
				Debug.LogError(typeof(T).ToString() + " is not created yet");
			}
			return instance;
		}
	}

	void Start()
	{
		if (instance == null)
		{
			instance = gameObject.GetComponent<T>();
			DontDestroyOnLoad(gameObject);
			OnStart();
		}
		else
		{
			//prevent to create a new gameobject if existed
			GameObject.Destroy(gameObject);
		}
	}

	public virtual void OnStart()
	{
		// override this to do something on Awake method
	}
}

