namespace Pop.Player
{
    /// <summary>
    /// 플레이어 상태. Attached --벽/천장--> Stunned --착지--> Normal, Attached --바닥--> Normal,
    /// 어디서든 낙사 → Dead → (즉시 리스폰) Normal. 모두 PlayerController.StateChanged로 나간다.
    /// </summary>
    public enum PlayerState
    {
        Normal,
        Attached,
        Flying,
        Stunned,
        Falling,
        Dead
    }
}
