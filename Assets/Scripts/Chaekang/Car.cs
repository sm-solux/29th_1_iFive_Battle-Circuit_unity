using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using Firebase.Auth;

public class Car : MonoBehaviourPunCallbacks, IPunObservable
{
    private Slider HPBar;
    private TMP_Text HPText;
    private GameObject gameOverPanel;
    private TMP_Text gameOverTxt;
    private Button Break;
    private GameObject DashBtn;
    private GameObject joyStick;
    private GameObject speedBar;

    public GameObject fire;
    public GameObject smoke;

    public float maxHP;
    public float curHP;
    public float curSpeed;
    float maxSpeed;

    int countdown = 10;
    private bool isGameOver = false;
    private bool countdownStarted = false;

    FirestoreManager firestoreManager;

    private FirebaseAuth auth;
    private Rigidbody rb;

    private Queue<System.Action> actionQueue = new Queue<System.Action>();
    private bool isDataReady = false;

    private Coroutine countdownCoroutine;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        auth = FirebaseAuth.DefaultInstance;
        firestoreManager = GameManager.Instance.firestoreManager;

        HPBar = FindInActiveObjectByName("HPBar")?.GetComponent<Slider>();
        HPText = FindInActiveObjectByName("HPText")?.GetComponent<TMP_Text>();
        gameOverPanel = FindInActiveObjectByName("GameOverPanel");
        Break = FindInActiveObjectByName("Break")?.GetComponent<Button>();
        DashBtn = FindInActiveObjectByName("Dash");
        joyStick = FindInActiveObjectByName("Joystick");
        speedBar = FindInActiveObjectByName("SpeedBar");

        if (HPBar == null)
        {
            Debug.LogError("HPBar not found or inactive.");
        }
        else
        {
            Debug.Log("HPBar found and assigned");
        }

        if (gameOverPanel != null)
        {
            gameOverTxt = gameOverPanel.transform.Find("RestartTxt")?.GetComponent<TMP_Text>();
        }

        if (Break == null)
        {
            Debug.LogError("Break button not found or inactive.");
        }

        if (joyStick == null)
        {
            Debug.LogError("Joystick not found or inactive.");
        }

        if (HPText == null)
        {
            Debug.LogError("HPText not found or inactive.");
        }

        actionQueue.Enqueue(() => {
            maxHP = firestoreManager.Hp;
            curHP = maxHP;
            SetMaxHealth(maxHP);
        });

        actionQueue.Enqueue(() => {
            maxSpeed = firestoreManager.MaxSpeed;
        });

        firestoreManager.OnDataReady += HandleDataReady;
    }

    private void HandleDataReady()
    {
        isDataReady = true;

        while (actionQueue.Count > 0)
        {
            actionQueue.Dequeue().Invoke();
        }
    }

    private GameObject FindInActiveObjectByName(string name)
    {
        Transform[] objs = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform obj in objs)
        {
            if (obj.hideFlags == HideFlags.None && obj.name == name)
            {
                return obj.gameObject;
            }
        }
        return null;
    }

    public void SetMaxHealth(float health)
    {
        HPBar.maxValue = health;
        HPBar.value = health;
    }

    public void GetDamaged(float damage)
    {
        curHP -= (float)damage;
        PhotonView myPhotonView = GetComponent<PhotonView>();
        int myPhotonViewID = myPhotonView != null ? myPhotonView.ViewID : -1;

        if (HPBar != null)
        {
            HPBar.value = curHP;
        }
        else
        {
            HPBar = FindInActiveObjectByName("HPBar")?.GetComponent<Slider>();
            if (HPBar == null)
            {
                Debug.LogError("GetDamaged: Failed to reassign HPBar.");
            }
            else
            {
                HPBar.value = curHP;
            }
        }
        UpdateHPText();
    }

    private void UpdateHPText()
    {
        if (HPText != null)
        {
            HPText.text = string.Format("{0}/{1}", (int)curHP, (int)maxHP);
        }
        else
        {
            Debug.LogError("UpdateHPText: HPText is null");

            HPText = FindInActiveObjectByName("HPText")?.GetComponent<TMP_Text>();
            if (HPText == null)
            {
                Debug.LogError("UpdateHPText: Failed to reassign HPText");
            }
            else
            {
                Debug.Log("UpdateHPText: Successfully reassigned HPText");
                HPText.text = string.Format("{0}/{1}", (int)curHP, (int)maxHP);
            }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        bool thisCarIsDashing = GameManager.Instance.dash.isDash;

        if (collision.transform.CompareTag("Team Red"))
        {
            PhotonView myPhotonView = GetComponent<PhotonView>();
            PhotonView otherPhotonView = collision.transform.GetComponent<PhotonView>();

            int myPhotonViewID = myPhotonView != null ? myPhotonView.ViewID : -1;
            int otherPhotonViewID = otherPhotonView != null ? otherPhotonView.ViewID : -1;

            // Play SFX
            AudioManager.instance.PlaySfx(AudioManager.Sfx.StartBtn);

            if (otherPhotonView != null && otherPhotonViewID != myPhotonViewID)
            {
                float damage = curSpeed * 3;
                if (thisCarIsDashing)
                {
                    damage *= 2;
                }

                Debug.Log($"Collided with car. Other PhotonView ID: {otherPhotonViewID}, Damage: {damage}, My PhotonView ID: {myPhotonViewID}");
                otherPhotonView.RPC("ReduceHP", RpcTarget.All, (double)damage);
            }
        }
    }

    [PunRPC]
    public void ReduceHP(double damage)
    {
        GetDamaged((float)damage);
    }

    private void Update()
    {
        if (!isDataReady) return;

        curSpeed = NetworkPlayer.speed;

        if (curSpeed >= maxSpeed)
        {
            curSpeed = maxSpeed;
        }

        if (curHP >= maxHP)
        {
            curHP = maxHP;
        }
        else if (curHP < 0)
        {
            curHP = 0;
        }

        if (HPBar != null)
        {
            HPBar.value = curHP;
        }

        UpdateHPText();

        if (curHP <= 0 && !isGameOver)
        {
            isGameOver = true;

            if (countdownCoroutine != null)
            {
                StopCoroutine(countdownCoroutine);
                countdownCoroutine = null;
            }

            GameOver();
        }

        if (Mathf.Abs(transform.eulerAngles.z) > 80 && curSpeed < 0.03)
        {
            Vector3 newRotation = transform.eulerAngles;
            newRotation.z = 0;
            transform.eulerAngles = newRotation;
        }
    }

    private void GameOver()
    {
        Debug.Log("Game Over");
        NetworkPlayer.speed = 0;
        smoke.SetActive(true);
        gameOverPanel.SetActive(true);

        if (!countdownStarted)
        {
            countdownStarted = true;
            Debug.Log("Starting RestartCountdown coroutine");
            countdownCoroutine = StartCoroutine(RestartCountdown());
            Debug.Log("RestartCountdown coroutine started");
        }
    }

    private IEnumerator RestartCountdown()
    {
        Debug.Log("RestartCountdown start");
        HPBar.gameObject.SetActive(false);
        Break.gameObject.SetActive(false);
        DashBtn.gameObject.SetActive(false);
        joyStick.gameObject.SetActive(false);
        speedBar.gameObject.SetActive(false);

        while (countdown > 0)
        {
            gameOverTxt.text = $"Restart in {countdown} seconds";
            yield return new WaitForSeconds(1f);
            countdown--;
        }

        RespawnPlayer();

        gameOverPanel.SetActive(false);

        HPBar.gameObject.SetActive(true);
        Break.gameObject.SetActive(true);
        DashBtn.gameObject.SetActive(true);
        joyStick.gameObject.SetActive(true);
        speedBar.gameObject.SetActive(true);

        countdown = 10;
        curHP = maxHP;

        countdownCoroutine = null;
        yield return null;
    }

    private void RespawnPlayer()
    {
        int spawnIndex = PhotonNetwork.LocalPlayer.ActorNumber - 1;
        Transform spawnPoint = SpawnManager.Instance.GetSpawnPoint(spawnIndex);

        if (spawnPoint != null)
        {
            PhotonView photonView = GetComponent<PhotonView>();
            if (photonView != null && photonView.IsMine)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                transform.position = spawnPoint.position;
                transform.rotation = spawnPoint.rotation;
                isGameOver = false;
                countdownStarted = false;
            }
            else
            {
                photonView.RPC("ResetPlayerPosition", photonView.Owner, spawnPoint.position, spawnPoint.rotation);
            }
        }
        else
        {
            Debug.LogError("Spawn point not found for index: " + spawnIndex);
        }
    }

    [PunRPC]
    private void ResetPlayerPosition(Vector3 position, Quaternion rotation)
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position = position;
        transform.rotation = rotation;
        isGameOver = false;
        countdownStarted = false;
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(curHP);
            stream.SendNext(curSpeed);
        }
        else
        {
            curHP = (float)stream.ReceiveNext();
            curSpeed = (float)stream.ReceiveNext();
        }
    }
}
