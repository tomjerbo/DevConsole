using System;
using Jerbo.Tools;
using UnityEngine;
using UnityEngine.Events;

public class TestScript : MonoBehaviour
{
    public float myFloat;
    public string myString;
    public bool myBool;
    public string myProperty { get; set; }
    public UnityEvent myUnityEvent;
    public event Action myEventAction;
    public Action myAction;
    [DevCommand] public void MyMethod(){ Debug.Log("Piss"); }
    [DevCommand] public void MethodWithArg(string name, int age){ Debug.Log("Piss"); }
    [DevCommand] public void MethodWithArg_SO(ScriptableObject inObject){ Debug.Log("Piss"); }
    public Transform myTransform;
    
    [DevCommand]
    public void CallMe()
    {
        Debug.Log("My number is 696969420");
    }
    void Awake()
    {
        myEventAction += () => { Debug.Log("Event action called"); };
        myAction += () => { Debug.Log("Single Action called"); };
    }
    
}
