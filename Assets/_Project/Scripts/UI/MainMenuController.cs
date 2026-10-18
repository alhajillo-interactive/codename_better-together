using System;
using BetterTogether.Networking;
using BetterTogether.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [SerializeField] Button createRoomBTN;
    [SerializeField] Button joinRoomBTN;
    [SerializeField] Button creditsBTN;
    [SerializeField] Button exitBTN;
    [SerializeField] Button joinBTN;
    [SerializeField] Button creditsCloseBTN;
    [SerializeField] GameObject credits;
    [SerializeField] GameObject mainMenu;
    [SerializeField] GameObject joinUI;
    [SerializeField] TMP_InputField roomName;
    [SerializeField] TMP_InputField playerName;





    void OnEnable()
    {
        createRoomBTN.onClick.AddListener(OpenJoinMenu);
        joinRoomBTN.onClick.AddListener(OpenJoinMenu);
        creditsBTN.onClick.AddListener(ShowCredits);
        exitBTN.onClick.AddListener(ExitGame);
        joinBTN.onClick.AddListener(JoinOrCreate);
        creditsCloseBTN.onClick.AddListener(CreditClose);




        joinUI.SetActive(false);
        mainMenu.SetActive(true);
        credits.SetActive(false);
    }

    private void CreditClose()
    {
        credits.SetActive(false);
    }

    private void OpenJoinMenu()
    {
        mainMenu.SetActive(false);
        joinUI.SetActive(true);
    }

    private void ExitGame()
    {
        Application.Quit();
    }

    private void JoinOrCreate()
    {
        ConnectionManager connectionManager = FindAnyObjectByType<ConnectionManager>();
        _ = connectionManager.JoinOrCreate(roomName.text);
        LocalPlayerProfile.Name = playerName.text;
    }

    void OnDisable()
    {
        createRoomBTN.onClick.RemoveListener(JoinOrCreate);
        joinRoomBTN.onClick.RemoveListener(JoinOrCreate);
        creditsBTN.onClick.RemoveListener(ShowCredits);
        exitBTN.onClick.RemoveListener(ExitGame);
        creditsCloseBTN.onClick.RemoveListener(CreditClose);


    }
    private void ShowCredits()
    {
        credits.SetActive(true);
    }

}
