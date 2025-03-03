using System;
using UnityEngine;
using UnityEngine.Events;

public class TestScript : MonoBehaviour
{
    [AddCommand] public float myFloat;
    [AddCommand] public string myString;
    [AddCommand] public bool myBool;
    [AddCommand] public string myProperty { get; set; }
    [AddCommand] public UnityEvent myUnityEvent;
    [AddCommand] public event Action myEventAction;
    [AddCommand] public Action myAction;
    [AddCommand] public void MyMethod(){ Debug.Log("Piss"); }
    [AddCommand] public void MethodWithArg(string name, int age){ Debug.Log("Piss"); }
    [AddCommand] public void MethodWithArg_SO(ScriptableObject inObject){ Debug.Log("Piss"); }
    [AddCommand] public Transform myTransform;
    
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
