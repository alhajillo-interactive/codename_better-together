namespace BetterTogether.Interaction
{
    /// <summary>
    /// An <see cref="IInteractable"/> that some players must use by holding the interact key instead of tapping it.
    /// The hold is local input and presentation: the interactor measures it and calls <see cref="OnHoldComplete"/> once.
    /// </summary>
    public interface IHoldInteractable
    {
        /// <summary>Seconds the key must be held for this interactor; 0 or less means a normal tap (<see cref="IInteractable.Interact"/>).</summary>
        float GetHoldTime(PlayerInteractor interactor);

        /// <summary>The key was held long enough while <see cref="IInteractable.CanInteract"/> stayed true. Runs on the interactor's own client.</summary>
        void OnHoldComplete(PlayerInteractor interactor);
    }
}
