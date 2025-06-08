using UnityEngine;
using UnityEngine.U2D.Animation;

public class SkullAnimation : MonoBehaviour
{
    [SerializeField] private SpriteResolver spriteResolver;
    [SerializeField] private float frameRate = 6f;

    private float animationTimer = 0f;
    private int currentFrame = 0;
    private bool isWalking = false;

    private enum Direction { Down, Up, Left, Right }
    private Direction currentDirection = Direction.Down;

    private string[][] directionLabels = new string[4][] {
        new string[] { "SkullAnim_0", "SkullAnim_2", "SkullAnim_4", "SkullAnim_6" }, // Down
        new string[] { "SkullAnim_1", "SkullAnim_3", "SkullAnim_5", "SkullAnim_7" }, // Up
        new string[] { "SkullAnim_1", "SkullAnim_3", "SkullAnim_5", "SkullAnim_7" }, // Left
        new string[] { "SkullAnim_0", "SkullAnim_2", "SkullAnim_4", "SkullAnim_6" }    // Right
    };

    void Start()
    {
        if (spriteResolver == null)
            spriteResolver = GetComponent<SpriteResolver>();
    }

    public void UpdateAnimation(Vector3 velocity)
    {
        isWalking = velocity.magnitude > 0.05f;

        if (isWalking)
        {
            if (Mathf.Abs(velocity.x) > Mathf.Abs(velocity.y))
                currentDirection = velocity.x > 0 ? Direction.Right : Direction.Left;
            else
                currentDirection = velocity.y > 0 ? Direction.Up : Direction.Down;
        }

        AnimateWalk();
    }

    void AnimateWalk()
    {
        string[] currentLabels = directionLabels[(int)currentDirection];

        if (!isWalking)
        {
            spriteResolver.SetCategoryAndLabel("Walk", currentLabels[0]);
            return;
        }

        animationTimer += Time.deltaTime;
        if (animationTimer >= 1f / frameRate)
        {
            animationTimer = 0f;
            currentFrame = (currentFrame + 1) % currentLabels.Length;
            spriteResolver.SetCategoryAndLabel("Walk", currentLabels[currentFrame]);
        }
    }
}
