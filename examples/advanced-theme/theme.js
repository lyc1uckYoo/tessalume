registerTheme({
  async mount(context) {
    const widget = document.createElement("section");
    widget.className = "example-theme-widget";

    const image = document.createElement("img");
    image.src = context.assetDataUrl("emblem");
    image.alt = "";

    const copy = document.createElement("span");
    const title = document.createElement("b");
    title.textContent = context.config.label ?? "Open Theme";
    const message = document.createElement("small");
    message.textContent = context.config.message ?? `Mode: ${context.mode}`;
    copy.append(title, message);
    widget.append(image, copy);
    context.root.appendChild(widget);

    context.addCleanup(() => {
      document.documentElement.removeAttribute("data-example-theme-mounted");
    });
    document.documentElement.setAttribute("data-example-theme-mounted", "true");
  },

  async unmount() {
    // The Studio-owned root, styles, observers, timers and registered cleanups
    // are removed automatically after this hook returns.
  }
});
