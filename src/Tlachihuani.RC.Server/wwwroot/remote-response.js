/* Tlamatini-RC — Angela López Mendoza · XAIHT
 * Render the main application's prepared history HTML. Never evaluate model markup.
 * The bundled DOMPurify is the same pinned 3.4.16 already carried by Tlamatini.
 */
(() => {
    'use strict';
    const BOUNDARY = '<!--TLAMATINI_EXEC_REPORT_BOUNDARY-->';
    const TAGS = 'a abbr b blockquote br caption code col colgroup dd del details div dl dt em figcaption figure h1 h2 h3 h4 h5 h6 hr i img ins kbd li mark ol p pre s samp small span strong sub summary sup table tbody td tfoot th thead time tr u ul'.split(' ');
    const ATTRS = 'href src alt title class style align valign width height colspan rowspan scope start reversed open datetime data-rc-file data-rc-filename'.split(' ');
    const STYLES = new Set(('color background background-color background-image background-size background-position background-repeat '
        + 'border border-top border-right border-bottom border-left border-color border-style border-width border-radius '
        + 'border-top-color border-right-color border-bottom-color border-left-color border-top-width border-right-width border-bottom-width border-left-width '
        + 'border-top-left-radius border-top-right-radius border-bottom-left-radius border-bottom-right-radius border-collapse border-spacing '
        + 'font font-family font-size font-style font-weight font-variant line-height letter-spacing word-spacing '
        + 'text-align text-decoration text-decoration-color text-decoration-line text-decoration-style text-indent text-transform text-shadow '
        + 'white-space overflow-wrap word-break tab-size vertical-align '
        + 'padding padding-top padding-right padding-bottom padding-left margin margin-top margin-right margin-bottom margin-left '
        + 'width max-width height max-height min-height box-shadow box-sizing '
        + 'display flex flex-direction flex-wrap align-items align-content justify-content gap row-gap column-gap '
        + 'grid-template-columns grid-template-rows table-layout caption-side list-style-type list-style-position').split(' '));
    const COPY = {
        en: { save: 'Save file', saving: 'Downloading…', started: 'Download started. Check your downloads.',
            failed: 'Could not download this file. Try again.', unavailable: 'This file is not available for download.',
            image: 'Image unavailable', table: 'Response table; scroll horizontally if needed',
            truncated: 'This response was shortened during delivery.', render: 'Formatted display is unavailable. The text is shown below.' },
        es: { save: 'Guardar archivo', saving: 'Descargando…', started: 'La descarga ha comenzado. Revisa tus descargas.',
            failed: 'No se pudo descargar este archivo. Inténtalo de nuevo.', unavailable: 'Este archivo no está disponible para descargar.',
            image: 'Imagen no disponible', table: 'Tabla de la respuesta; desplázate horizontalmente si es necesario',
            truncated: 'Esta respuesta se acortó durante la entrega.', render: 'El formato no está disponible. El texto se muestra a continuación.' },
    };

    function safeStyle(element) {
        const source = element.style;
        const declarations = [...source].map(name => [name, source.getPropertyValue(name)]);
        element.removeAttribute('style');
        for (const [name, value] of declarations) {
            // Reject every CSS escape and network/computed-attribute function. Only
            // presentation within the response may survive; no positioning or overlays.
            if (!STYLES.has(name) || /[\\\u0000-\u001f]|url\s*\(|expression\s*\(|(?:var|attr|image-set|image|paint|element)\s*\(|@/i.test(value)) continue;
            if (name === 'display' && !/^(?:block|inline|inline-block|flex|inline-flex|grid|inline-grid|table|table-row|table-cell|table-row-group|table-header-group|table-footer-group|table-caption|list-item)$/.test(value)) continue;
            element.style.setProperty(name, value);
        }
    }

    function externalLink(href) {
        try {
            const url = new URL(href);
            return ['http:', 'https:', 'mailto:'].includes(url.protocol) ? url.href : null;
        } catch (_) { return null; }
    }

    function filenameSafe(name) {
        return typeof name === 'string' && name.length > 0 && name.length <= 512
            && !/[\u0000-\u001f\u007f\\/:]/.test(name) && name !== '.' && name !== '..';
    }

    function fileControl(element, options, words) {
        const ticket = element.getAttribute('data-rc-file') || '';
        const name = element.getAttribute('data-rc-filename') || '';
        element.removeAttribute('data-rc-file');
        element.removeAttribute('data-rc-filename');
        const container = document.createElement('span');
        container.className = 'rc-file';
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'rc-file-download';
        button.textContent = words.save + ': ' + (filenameSafe(name) ? name : '…');
        const status = document.createElement('span');
        status.className = 'rc-file-status';
        status.setAttribute('role', 'status');
        status.setAttribute('aria-live', 'polite');
        if (!filenameSafe(name) || !ticket || ticket.length > 4096 || /[\u0000-\u0020\u007f]/.test(ticket)
                || typeof options.download !== 'function') {
            button.disabled = true;
            status.textContent = words.unavailable;
        } else {
            button.addEventListener('click', async () => {
                if (button.disabled) return;
                button.disabled = true;
                button.setAttribute('aria-busy', 'true');
                button.textContent = words.saving + ' ' + name;
                status.textContent = '';
                status.classList.remove('error');
                try {
                    await options.download(ticket, name);
                    status.textContent = words.started;
                } catch (error) {
                    status.textContent = error && typeof error.message === 'string' && error.message ? error.message : words.failed;
                    status.classList.add('error');
                } finally {
                    button.disabled = false;
                    button.removeAttribute('aria-busy');
                    button.textContent = words.save + ': ' + name;
                }
            });
        }
        container.append(button, status);
        element.replaceWith(container);
    }

    function fragment(html, options, words) {
        const result = window.DOMPurify.sanitize(html, {
            ALLOWED_TAGS: TAGS, ALLOWED_ATTR: ATTRS,
            ALLOW_DATA_ATTR: false, ALLOW_ARIA_ATTR: false,
            RETURN_DOM_FRAGMENT: true,
        });
        for (const element of result.querySelectorAll('*')) {
            safeStyle(element);
            const classes = [...element.classList].filter(name => name === 'ascii-diagram'
                || /^(?:exec-report|exec-denied)(?:-[a-z0-9]+)*$/.test(name)
                || /^language-[a-z0-9_+-]+$/i.test(name));
            element.removeAttribute('class');
            if (classes.length) element.classList.add(...classes);
            if ((element.tagName === 'A' || element.tagName === 'SPAN') && element.hasAttribute('data-rc-file')) {
                fileControl(element, options, words);
                continue;
            }
            element.removeAttribute('data-rc-file');
            element.removeAttribute('data-rc-filename');
            if (element.tagName === 'A') {
                const href = externalLink(element.getAttribute('href') || '');
                element.removeAttribute('href');
                if (href) {
                    element.href = href;
                    element.target = '_blank';
                    element.rel = 'noopener noreferrer';
                }
            }
            if (element.tagName === 'IMG') {
                const data = element.getAttribute('src') || '';
                element.removeAttribute('src');
                // Data raster images cannot issue authenticated requests, track a
                // viewer remotely, or execute an SVG document. All other sources fail closed.
                if (data.length <= 6 * 1024 * 1024 && /^data:image\/(?:png|jpe?g|gif|webp|avif|bmp);base64,[a-z0-9+/=\s]+$/i.test(data)) {
                    element.src = data;
                    element.loading = 'lazy';
                    element.decoding = 'async';
                } else {
                    const placeholder = document.createElement('span');
                    placeholder.className = 'rc-image-unavailable';
                    placeholder.textContent = element.getAttribute('alt') || words.image;
                    element.replaceWith(placeholder);
                }
            }
        }
        for (const table of [...result.querySelectorAll('table')]) {
            const wrapper = document.createElement('div');
            wrapper.className = 'rc-response-table';
            wrapper.tabIndex = 0;
            wrapper.setAttribute('role', 'region');
            wrapper.setAttribute('aria-label', words.table);
            table.replaceWith(wrapper);
            wrapper.append(table);
        }
        return result;
    }

    // Same measured contrast correction as main chat_table_contrast.js. Keep
    // authored readable colours and gradients; execution reports own their styles.
    function contrastTables(target) {
        function color(value) {
            const m = /rgba?\(([^)]+)\)/.exec(value || '');
            if (!m) return null;
            const p = m[1].split(',').map(Number);
            return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 };
        }
        function luminance(c) {
            const channel = v => (v /= 255) <= .03928 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4;
            return .2126 * channel(c.r) + .7152 * channel(c.g) + .0722 * channel(c.b);
        }
        for (const table of target.querySelectorAll('table')) {
            if (table.closest('.exec-report-table, .exec-report')) continue;
            for (const cell of table.querySelectorAll('td, th, caption')) {
                let background = null;
                for (let node = cell; node && node.nodeType === 1; node = node.parentElement) {
                    const style = getComputedStyle(node);
                    if (style.backgroundImage && style.backgroundImage !== 'none') break;
                    const c = color(style.backgroundColor);
                    if (c && c.a >= .5) { background = c; break; }
                }
                const foreground = color(getComputedStyle(cell).color);
                if (!background || !foreground) continue;
                const a = luminance(background), b = luminance(foreground);
                if ((Math.max(a, b) + .05) / (Math.min(a, b) + .05) < 3) cell.style.color = a > .35 ? '#0f172a' : '#f8fafc';
            }
        }
    }

    function render(target, value, options = {}) {
        const words = COPY[options.language === 'es' ? 'es' : 'en'];
        const data = value && typeof value === 'object' ? value : { text: String(value ?? '') };
        target.replaceChildren();
        target.classList.add('rc-rich-response');
        if (data.role === 'user' || data.format !== 'html' || typeof data.html !== 'string') {
            target.textContent = String(data.text ?? '');
        } else if (!window.DOMPurify?.sanitize) {
            const warning = document.createElement('p');
            warning.className = 'rc-render-warning';
            warning.textContent = words.render;
            const plain = document.createElement('div');
            plain.textContent = String(data.text ?? '');
            target.append(warning, plain);
        } else {
            try {
                const content = document.createElement('div');
                content.className = 'automated-message';
                const boundary = data.html.indexOf(BOUNDARY);
                const body = document.createElement('div');
                body.className = 'automated-message-body';
                body.append(fragment(boundary < 0 ? data.html : data.html.slice(0, boundary), options, words));
                content.append(body);
                if (boundary >= 0) {
                    const report = document.createElement('div');
                    report.className = 'automated-message-execreport';
                    report.append(fragment(data.html.slice(boundary + BOUNDARY.length), options, words));
                    content.append(report);
                }
                target.append(content);
                requestAnimationFrame(() => { if (target.isConnected) contrastTables(target); });
            } catch (_) {
                target.replaceChildren();
                const warning = document.createElement('p');
                warning.className = 'rc-render-warning';
                warning.textContent = words.render;
                const plain = document.createElement('div');
                plain.textContent = String(data.text ?? '');
                target.append(warning, plain);
            }
        }
        if (data.truncated) {
            const notice = document.createElement('p');
            notice.className = 'rc-response-truncated';
            notice.textContent = words.truncated;
            target.append(notice);
        }
        return target;
    }
    window.TlamatiniResponse = Object.freeze({ render });
})();
