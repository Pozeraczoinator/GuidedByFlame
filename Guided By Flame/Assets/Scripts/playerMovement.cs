using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class playerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;

    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private float stepInterval = 0.5f;

    private Rigidbody2D rb;
    private Vector2 moveInput;
    private Animator animator;
    private AudioSource audioSource;
    private Coroutine footstepCoroutine;

    private bool isSprinting = false;
    private bool canMove = true; // <<--- NOWE

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        if (!canMove)
        {
            rb.linearVelocity = Vector2.zero;

            if (footstepCoroutine != null)
            {
                StopCoroutine(footstepCoroutine);
                footstepCoroutine = null;
                audioSource.Stop();
            }

            animator.SetBool("isWalking", false);
            return;
        }

        float currentSpeed = isSprinting ? sprintSpeed : moveSpeed;
        rb.linearVelocity = moveInput * currentSpeed;

        if (moveInput != Vector2.zero)
        {
            if (footstepCoroutine == null)
                footstepCoroutine = StartCoroutine(PlayFootsteps());
        }
        else
        {
            if (footstepCoroutine != null)
            {
                StopCoroutine(footstepCoroutine);
                footstepCoroutine = null;
                audioSource.Stop();
            }
        }
    }

    public void Move(InputAction.CallbackContext context)
    {
        if (!canMove)
        {
            moveInput = Vector2.zero;
            return;
        }

        moveInput = context.ReadValue<Vector2>();

        animator.SetFloat("InputX", moveInput.x);
        animator.SetFloat("InputY", moveInput.y);

        if (context.canceled)
        {
            animator.SetBool("isWalking", false);
            animator.SetFloat("LastInputX", moveInput.x);
            animator.SetFloat("LastInputY", moveInput.y);
        }
        else
        {
            animator.SetBool("isWalking", true);
        }
    }

    public void Sprint(InputAction.CallbackContext context)
    {
        if (!canMove) return;

        if (context.performed)
        {
            isSprinting = true;
        }
        else if (context.canceled)
        {
            isSprinting = false;
        }
    }

    public void SetCanMove(bool value)
    {
        canMove = value;

        if (!canMove)
        {
            moveInput = Vector2.zero;
            rb.linearVelocity = Vector2.zero;
        }
    }

    private IEnumerator PlayFootsteps()
    {
        while (true)
        {
            if (footstepClips.Length > 0)
            {
                AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];
                audioSource.clip = clip;
                audioSource.Play();
            }

            yield return new WaitForSeconds(stepInterval);
        }
    }
}

