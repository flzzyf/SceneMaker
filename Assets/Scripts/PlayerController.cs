using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float speed = 7;

    Vector2 input;
    Animator animator;
    SpriteRenderer spriteRenderer;

    void Awake()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

        bool moving = input.x != 0;
        animator.SetBool("Walk", moving);

        if(moving)
        {
            if (input.x > 0 != spriteRenderer.flipX)
            {
                spriteRenderer.flipX = !spriteRenderer.flipX;
            }
        }
    }

    void FixedUpdate()
    {
        transform.Translate(Vector2.right * input.x * speed * Time.fixedDeltaTime);
    }
}
