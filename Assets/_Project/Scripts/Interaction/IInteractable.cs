namespace BetterTogether.Interaction
{
    /// <summary>
    /// Anything the local player can look at and use with the interact key.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Short label shown on the HUD, e.g. "Tomar leño".</summary>
        string Prompt { get; }

        bool CanInteract(PlayerInteractor interactor);

        void Interact(PlayerInteractor interactor);
    }
}
