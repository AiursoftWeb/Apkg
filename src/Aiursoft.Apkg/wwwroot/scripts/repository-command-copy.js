(() => {
    const messages = document.getElementById('repository-copy-messages');
    const text = (key, fallback) => messages?.querySelector(`[data-key="${key}"]`)?.textContent || fallback;

    document.querySelectorAll('[data-command-block]').forEach(block => {
        const button = block.querySelector('[data-copy-command]');
        const status = block.querySelector('[data-copy-status]');
        const pre = block.querySelector('pre');
        const code = pre.querySelector('code');

        button.addEventListener('click', async () => {
            if (button.disabled) return;
            button.disabled = true;
            button.setAttribute('aria-busy', 'true');
            status.textContent = '';
            status.classList.remove('text-warning');
            try {
                if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable');
                await navigator.clipboard.writeText(code.textContent);
                status.textContent = text('copied', 'Copied.');
            } catch {
                pre.focus({ preventScroll: true });
                const selection = window.getSelection();
                const range = document.createRange();
                range.selectNodeContents(code);
                selection?.removeAllRanges();
                selection?.addRange(range);
                status.classList.add('text-warning');
                status.textContent = text('failed', 'Copy failed. Copy the selected command manually.');
            } finally {
                button.disabled = false;
                button.removeAttribute('aria-busy');
            }
        });
    });
})();
