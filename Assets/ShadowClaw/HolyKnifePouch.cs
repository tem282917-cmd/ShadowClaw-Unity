using UnityEngine;

// Count changes are transactional: throwing spends one, a real relight grants one.
public sealed class HolyKnifePouch
{
    public const int Capacity=5;
    public int Count {get;private set;}
    public void Reset(int initial=2) {Count=Mathf.Clamp(initial,0,Capacity);}
    public bool Grant() {if(Count>=Capacity)return false;Count++;return true;}
    public bool Spend() {if(Count<=0)return false;Count--;return true;}
}
