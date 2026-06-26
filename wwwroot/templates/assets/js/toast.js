(function(window, document){
    'use strict';

    const DEFAULT_DURATION = 4000; // ms
    const CONTAINER_ID = 'rm-toast-container';

    // Inject styles once
    function injectStyles(){
        if (document.getElementById('rm-toast-styles')) return;
        const css = `
#${CONTAINER_ID} {
  position: fixed;
  top: 12px;
  left: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  z-index: 2147483647; /* very high */
  pointer-events: none; /* allow clicks through when no toast */
  width: 320px;
  max-width: calc(100% - 24px);
}

.rm-toast {
  pointer-events: auto; /* enable interaction for toasts */
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border-radius: 10px;
  box-shadow: 0 8px 24px rgba(17,24,39,0.12);
  color: #0f172a;
  font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
  font-size: 14px;
  line-height: 1.2;
  opacity: 0;
  transform: translateY(-8px) scale(.995);
  transition: opacity 260ms ease, transform 260ms ease;
  border: 1px solid rgba(15,23,42,0.06);
}

.rm-toast.show {
  opacity: 1;
  transform: translateY(0) scale(1);
}

.rm-toast .rm-toast-content {
  flex: 1 1 auto;
}

.rm-toast .rm-toast-close {
  background: transparent;
  border: none;
  color: inherit;
  cursor: pointer;
  font-size: 14px;
  padding: 6px;
  border-radius: 6px;
}

.rm-toast-success { background: linear-gradient(90deg, #ecfdf5, #f0fdf4); border-left: 4px solid #10b981; }
.rm-toast-error { background: linear-gradient(90deg, #fff1f2, #fff1f2); border-left: 4px solid #ef4444; }
.rm-toast-warning { background: linear-gradient(90deg, #fffbeb, #fffbeb); border-left: 4px solid #f59e0b; }

/* small icon */
.rm-toast .rm-toast-icon { width:20px; height:20px; flex:0 0 20px; }

/* responsive adjustments */
@media (max-width: 520px) {
  #${CONTAINER_ID} { left: 8px; top: 8px; width: calc(100% - 16px); }
  .rm-toast { padding: 10px; font-size: 13px; }
}
`;
        const style = document.createElement('style');
        style.id = 'rm-toast-styles';
        style.appendChild(document.createTextNode(css));
        document.head.appendChild(style);
    }

    // Ensure container exists
    function getContainer(){
        let container = document.getElementById(CONTAINER_ID);
        if (!container){
            container = document.createElement('div');
            container.id = CONTAINER_ID;
            document.body.appendChild(container);
        }
        return container;
    }

    // Icon SVGs
    const ICONS = {
        success: '<svg class="rm-toast-icon" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M20 6L9 17l-5-5" stroke="#059669" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
        error: '<svg class="rm-toast-icon" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M12 9v4m0 4h.01" stroke="#dc2626" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/><path d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" stroke="#dc2626" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
        warning: '<svg class="rm-toast-icon" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M12 8v4m0 4h.01" stroke="#b45309" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z" stroke="#b45309" stroke-width="0"/></svg>'
    };

    // Create toast element
    function createToast(message, type, duration){
        const container = getContainer();
        const toast = document.createElement('div');
        toast.className = 'rm-toast rm-toast-' + (type || 'success');

        toast.innerHTML = `
            ${ICONS[type] || ICONS.success}
            <div class="rm-toast-content">${escapeHtml(String(message))}</div>
            <button class="rm-toast-close" aria-label="Close">&times;</button>
        `;

        const closeBtn = toast.querySelector('.rm-toast-close');
        closeBtn.addEventListener('click', () => hideToast(toast));

        // click anywhere to close (optional)
        toast.addEventListener('click', function(e){
            if (e.target === closeBtn) return; // already handled
            // don't close when clicking links inside if any
            hideToast(toast);
        });

        container.appendChild(toast);

        // trigger show animation on next tick
        requestAnimationFrame(() => {
            toast.classList.add('show');
        });

        // auto close
        const t = typeof duration === 'number' ? duration : DEFAULT_DURATION;
        const timer = setTimeout(() => hideToast(toast), t);

        // cleanup when removed
        toast._rm_timer = timer;

        return toast;
    }

    function hideToast(toast){
        if (!toast) return;
        clearTimeout(toast._rm_timer);
        toast.classList.remove('show');
        // wait for transition
        setTimeout(() => {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
        }, 300);
    }

    // Escaping to avoid injection
    function escapeHtml(str){
        return str.replace(/[&<>\"']/g, function(tag){
            const chars = {
                '&': '&amp;',
                '<': '&lt;',
                '>': '&gt;',
                '"': '&quot;',
                "'": '&#39;'
            };
            return chars[tag] || tag;
        });
    }

    // Public API
    function showToast(message, type, duration){
        if (!message) return null;
        injectStyles();
        createToast(message, type || 'success', duration);
    }

    // alias
    function showMessage(message, type, duration){
        return showToast(message, type, duration);
    }

    // expose to window
    window.showToast = showToast;
    window.showMessage = showMessage;

})(window, document);
