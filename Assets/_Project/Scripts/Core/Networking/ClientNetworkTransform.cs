using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// NetworkTransform owner-autoritativo: è il client (owner) a spingere
/// posizione e rotazione al server, non viceversa.
///
/// Setup:
///   Sostituisce il componente NetworkTransform standard sul prefab del player.
///   Senza questo, il NetworkTransform server-autoritativo di default
///   riscrive la posizione del client ogni tick → movimento rallentato
///   e distanze server-side sempre errate (revive, trigger, ecc.).
/// </summary>
[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
