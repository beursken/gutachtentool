// Gutachtentool (Browser-Fassung): Browserspeicher, Herunterladen und Entwurfseditor.
// Alle Daten bleiben in der IndexedDB dieses Browsers; nichts wird an einen Server gesendet.
window.gutachtentool = (() => {
    'use strict';

    // ---------- IndexedDB: Pfad → Dateiinhalt (Uint8Array) ----------
    let db = null;

    function oeffnen() {
        if (db) return Promise.resolve(db);
        return new Promise((ok, fehler) => {
            const anfrage = indexedDB.open('gutachtentool', 1);
            anfrage.onupgradeneeded = () => anfrage.result.createObjectStore('dateien');
            anfrage.onsuccess = () => { db = anfrage.result; ok(db); };
            anfrage.onerror = () => fehler(anfrage.error);
        });
    }

    async function vorgang(modus, aktion) {
        const d = await oeffnen();
        return new Promise((ok, fehler) => {
            const t = d.transaction('dateien', modus);
            let ergebnis;
            const anfrage = aktion(t.objectStore('dateien'));
            if (anfrage) anfrage.onsuccess = () => { ergebnis = anfrage.result; };
            t.oncomplete = () => ok(ergebnis);
            t.onerror = () => fehler(t.error);
            t.onabort = () => fehler(t.error || new Error('Speichern abgebrochen (Speicher voll?)'));
        });
    }

    const speicher = {
        pfade: async () => (await vorgang('readonly', s => s.getAllKeys())) || [],
        lesen: p => vorgang('readonly', s => s.get(p)),
        schreiben: (p, daten) => vorgang('readwrite', s => s.put(daten, p)),
        loeschen: p => vorgang('readwrite', s => s.delete(p)),
        dauerhaft: async () => (navigator.storage && navigator.storage.persist) ? await navigator.storage.persist() : false,
        belegung: async () => {
            if (!navigator.storage || !navigator.storage.estimate) return null;
            const e = await navigator.storage.estimate();
            return [e.usage || 0, e.quota || 0];
        },
    };

    // ---------- Herunterladen ----------
    function herunterladen(name, typ, daten) {
        const url = URL.createObjectURL(new Blob([daten], { type: typ }));
        const a = document.createElement('a');
        a.href = url;
        a.download = name;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
    }

    function fokus(id) {
        const el = document.getElementById(id);
        if (el) { el.focus(); if (el.select && el.tagName === 'INPUT') el.select(); }
    }

    // ---------- Entwurfseditor (contenteditable, nur <b>, <i>, <br>) ----------
    let netz = null;          // DotNetObjectReference des WebEditor
    let auswahl = null;       // letzte Cursorposition im Editor

    function imEditor(el, knoten) { return knoten && (knoten === el || el.contains(knoten)); }

    function cursorMerken(el) {
        const s = document.getSelection();
        if (s && s.rangeCount && imEditor(el, s.anchorNode)) auswahl = s.getRangeAt(0).cloneRange();
    }

    function cursorHerstellen(el) {
        el.focus();
        const s = document.getSelection();
        if (s.rangeCount && imEditor(el, s.anchorNode)) return;
        s.removeAllRanges();
        if (auswahl && imEditor(el, auswahl.startContainer)) {
            s.addRange(auswahl);
        } else {
            const r = document.createRange();
            r.selectNodeContents(el);
            r.collapse(false);
            s.addRange(r);
        }
    }

    function textVorCursor(el, laenge) {
        const s = document.getSelection();
        if (!s.rangeCount) return '';
        const r = document.createRange();
        r.selectNodeContents(el);
        r.setEnd(s.getRangeAt(0).startContainer, s.getRangeAt(0).startOffset);
        const t = r.toString();
        return t.substring(Math.max(0, t.length - laenge));
    }

    function einfuegenRoh(text) {
        // Eingefügte Bausteine sind nie fett oder kursiv (wie in der Windows-Fassung).
        if (document.queryCommandState('bold')) document.execCommand('bold');
        if (document.queryCommandState('italic')) document.execCommand('italic');
        document.execCommand('insertText', false, text);
    }

    function laeufe(id) {
        const el = document.getElementById(id);
        const aus = [];
        if (!el) return aus;
        const anfuegen = (t, f, k) => {
            if (!t) return;
            const l = aus[aus.length - 1];
            if (l && l.f === f && l.k === k) l.t += t; else aus.push({ t: t, f: f, k: k });
        };
        const endetMitUmbruch = () => aus.length === 0 || aus[aus.length - 1].t.endsWith('\n');
        const gehe = (knoten, f, k) => {
            for (const n of knoten.childNodes) {
                if (n.nodeType === Node.TEXT_NODE) { anfuegen(n.nodeValue.replace(/​/g, '').replace(/ /g, ' '), f, k); continue; }
                if (n.nodeType !== Node.ELEMENT_NODE) continue;
                const tag = n.tagName;
                if (tag === 'BR') { anfuegen('\n', f, k); continue; }
                const st = n.style || {};
                let nf = f || tag === 'B' || tag === 'STRONG' || st.fontWeight === 'bold' || parseInt(st.fontWeight, 10) >= 600;
                if (st.fontWeight === 'normal' || st.fontWeight === '400') nf = false;
                let nk = k || tag === 'I' || tag === 'EM' || st.fontStyle === 'italic';
                if (st.fontStyle === 'normal') nk = false;
                const block = tag === 'DIV' || tag === 'P' || tag === 'LI';
                if (block && !endetMitUmbruch()) anfuegen('\n', f, k);
                gehe(n, nf, nk);
                if (block && !endetMitUmbruch()) anfuegen('\n', f, k);
            }
        };
        gehe(el, false, false);
        // Abschließende Leerzeilen entfernen (der Editor hält ein <br> am Ende)
        while (aus.length) {
            const l = aus[aus.length - 1];
            l.t = l.t.replace(/\n+$/, '');
            if (l.t.length) break;
            aus.pop();
        }
        return aus;
    }

    const editor = {
        anmelden(id, ref) {
            netz = ref;
            const el = document.getElementById(id);
            if (!el || el.dataset.angemeldet) return;
            el.dataset.angemeldet = '1';
            try { document.execCommand('styleWithCSS', false, false); } catch (e) { /* ältere Browser */ }
            document.addEventListener('selectionchange', () => cursorMerken(el));
            el.addEventListener('input', () => netz && netz.invokeMethodAsync('EntwurfGeaendert'));
            el.addEventListener('paste', e => {
                // Nur reiner Text: keine Formatierung aus Word oder Webseiten übernehmen.
                e.preventDefault();
                const t = (e.clipboardData || window.clipboardData).getData('text/plain');
                document.execCommand('insertText', false, t);
            });
            el.addEventListener('keydown', e => {
                if (e.key === 'Enter' && !e.ctrlKey && !e.metaKey) {
                    e.preventDefault();
                    document.execCommand('insertLineBreak');
                    return;
                }
                if ((e.ctrlKey || e.metaKey) && !e.altKey && (e.key === 'b' || e.key === 'i')) {
                    e.preventDefault();
                    document.execCommand(e.key === 'b' ? 'bold' : 'italic');
                    return;
                }
                if (e.key === 'Tab' && !e.shiftKey && !e.ctrlKey && !e.altKey && !e.metaKey && netz) {
                    const treffer = netz.invokeMethod('KuerzelPruefen', textVorCursor(el, 30));
                    if (!treffer) return;
                    e.preventDefault();
                    const s = document.getSelection();
                    for (let i = 0; i < treffer.laenge; i++) s.modify('extend', 'backward', 'character');
                    document.execCommand('delete');
                    editor.baustein(id, treffer.text);
                }
            });
        },
        setzen(id, html) {
            const el = document.getElementById(id);
            if (el) { el.innerHTML = html; auswahl = null; }
        },
        laeufe,
        baustein(id, text) {
            const el = document.getElementById(id);
            if (!el) return;
            cursorHerstellen(el);
            const s = document.getSelection();
            if (s.rangeCount && !s.isCollapsed) s.collapseToEnd();
            const davor = textVorCursor(el, 1);
            const praefix = davor.length > 0 && !/\s/.test(davor) ? ' ' : '';
            const suffix = /[.!?:]$/.test(text) ? ' ' : '. ';
            einfuegenRoh(praefix + text + suffix);
            cursorMerken(el);
        },
        formatieren(id, befehl) {
            const el = document.getElementById(id);
            if (!el) return;
            cursorHerstellen(el);
            document.execCommand(befehl);
        },
    };

    // ---------- Einfache Textfelder (Anmerkungen im Reiter „Rohpunkte“) ----------
    const feld = {
        einfuegen(id, text) {
            const el = document.getElementById(id);
            if (!el) return null;
            const start = el.selectionStart ?? el.value.length;
            const ende = el.selectionEnd ?? start;
            const davor = el.value.substring(0, start);
            const praefix = davor.length > 0 && !/\s$/.test(davor) ? ' ' : '';
            const suffix = /[.!?:]$/.test(text) ? ' ' : '. ';
            const neu = praefix + text + suffix;
            el.value = davor + neu + el.value.substring(ende);
            const pos = start + neu.length;
            el.focus();
            el.setSelectionRange(pos, pos);
            return el.value;
        },
    };

    // ---------- Sicherung beim Verlassen ----------
    function beimVerlassen(ref) {
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'hidden') ref.invokeMethodAsync('JetztSichern');
        });
        window.addEventListener('pagehide', () => ref.invokeMethodAsync('JetztSichern'));
    }

    function neuLaden() { location.reload(); }

    return { speicher, herunterladen, fokus, editor, feld, beimVerlassen, neuLaden };
})();
