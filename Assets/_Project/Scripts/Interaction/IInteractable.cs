namespace BetterTogether.Interaction
{
    /// <summary>
    /// Anything the local player can look at and use with the interact key.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Short label shown on the HUD for this interactor, e.g. "Tomar leño".</summary>
        string GetPrompt(PlayerInteractor interactor);

        bool CanInteract(PlayerInteractor interactor);

        void Interact(PlayerInteractor interactor);
    }
}
