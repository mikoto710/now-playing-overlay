export async function finishAnimation(animation: Animation): Promise<void> {
  try {
    await animation.finished;
  } catch {
    // A newer view state is allowed to cancel an animation that is still running.
  }
}

export function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds));
}
