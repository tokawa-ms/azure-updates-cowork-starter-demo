for (const button of document.querySelectorAll("[data-copy]")) {
  button.addEventListener("click", async () => {
    const target = document.getElementById(button.dataset.copy);
    if (!target) return;

    try {
      await navigator.clipboard.writeText(target.textContent);
      button.textContent = "コピーしました";
      window.setTimeout(() => { button.textContent = "コピー"; }, 1800);
    } catch (error) {
      button.textContent = "コピーできません";
      console.error("クリップボードへの書き込みに失敗しました。", error);
    }
  });
}
