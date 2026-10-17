export function isEditableElement(target: EventTarget | null): boolean {
  if (!target || typeof target !== "object") return false;
  const element = target as Partial<HTMLElement>;
  const tagName = typeof element.tagName === "string" ? element.tagName.toLowerCase() : "";
  if (tagName === "input" || tagName === "textarea" || tagName === "select") return true;
  if (Boolean(element.isContentEditable)) return true;
  if (typeof element.closest === "function") {
    return Boolean(element.closest("input, textarea, select, [contenteditable='true']"));
  }
  return false;
}

export function shouldSuppressShortcut(event: {
  target?: EventTarget | null;
  ctrlKey?: boolean;
  metaKey?: boolean;
  altKey?: boolean;
}): boolean {
  if (isEditableElement(event.target ?? null)) return true;
  if (Boolean(event.ctrlKey || event.metaKey || event.altKey)) return true;
  return false;
}

