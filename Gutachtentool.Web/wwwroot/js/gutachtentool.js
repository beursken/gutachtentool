// Gutachtentool (Browser- und Mac-Fassung): Browserspeicher, Herunterladen und Entwurfseditor.
// Browser: alle Daten bleiben in der IndexedDB dieses Browsers; nichts wird an einen Server gesendet.
// Mac: dieselbe Datei im nativen Fenster (WebKit); Speicher und Herunterladen werden dort nicht genutzt.
// Nur asynchrone Aufrufe nach .NET (im Mac-Fenster gibt es keine synchronen).
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
    let netz = null;          // DotNetObjectReference des EntwurfEditor
    let auswahl = null;       // letzte Cursorposition im Editor
    let kuerzel = {};         // festes Kürzel (klein, ohne #) → Bausteintext
    const kuerzelVorCursor = /(?<![\p{L}\d_\-])#?([\p{L}\d_\-]{1,24})$/u;

    function melden(id) {
        if (netz) netz.invokeMethodAsync('EntwurfEingabe', laeufe(id));
    }

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
            el.addEventListener('input', e => { melden(id); kuerzelHinweis(el); tippfehler(el, e.inputType, e.data); });
            el.addEventListener('compositionend', e => tippfehler(el, 'insertText', e.data));
            el.addEventListener('keyup', e => { if (e.key.startsWith('Arrow')) kuerzelHinweis(el); });
            el.addEventListener('blur', () => { hinweisVerbergen(); normHinweisVerbergen(); });
            // Normzitate: Mauszeiger zeigt den Normtext; Strg+Klick öffnet ihn bzw. springt bei „Frage 1“ zur Lösung.
            el.addEventListener('mousemove', e => {
                normHinweisVerbergen();
                clearTimeout(normTakt);
                normTakt = setTimeout(async () => {
                    const s = textAnPunkt(el, e.clientX, e.clientY);
                    if (!s || !seite) return;
                    const t = await seite.invokeMethodAsync('NormHinweisAn', s.text, s.stelle);
                    if (t) normHinweisZeigen(t + '\n\nStrg+Klick: Volltext in der Seitenleiste', e.clientX, e.clientY);
                }, 450);
            });
            el.addEventListener('mouseleave', () => { clearTimeout(normTakt); normHinweisVerbergen(); });
            el.addEventListener('click', async e => {
                if (!(e.ctrlKey || e.metaKey) || !seite) return;
                const s = textAnPunkt(el, e.clientX, e.clientY);
                if (!s) return;
                if (await seite.invokeMethodAsync('EditorStrgKlick', s.text, s.stelle)) normHinweisVerbergen();
            });
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
                if (e.key === 'Escape') { hinweisVerbergen(); return; }
                if (e.key === 'Tab' && !e.shiftKey && !e.ctrlKey && !e.altKey && !e.metaKey) {
                    hinweisVerbergen();
                    const m = kuerzelVorCursor.exec(textVorCursor(el, 30));
                    const text = m ? kuerzel[m[1].toLowerCase()] : undefined;
                    if (!text) return;
                    e.preventDefault();
                    const s = document.getSelection();
                    for (let i = 0; i < m[0].length; i++) s.modify('extend', 'backward', 'character');
                    document.execCommand('delete');
                    editor.baustein(id, text);
                }
            });
        },
        setzen(id, html) {
            const el = document.getElementById(id);
            if (el) { el.innerHTML = html; auswahl = null; }
        },
        laeufe,
        kuerzel(liste) { kuerzel = liste || {}; },
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

    // ---------- Normtext beim Überfahren (Editor) ----------
    let seite = null, normTakt = 0, normHinweis = null;
    function normHinweisVerbergen() { if (normHinweis) normHinweis.hidden = true; }
    function normHinweisZeigen(text, x, y) {
        if (!normHinweis) {
            normHinweis = document.createElement('div');
            normHinweis.className = 'normhinweis';
            document.body.appendChild(normHinweis);
        }
        normHinweis.textContent = text;
        normHinweis.style.left = Math.max(8, Math.min(x + 12, window.innerWidth - 500)) + 'px';
        normHinweis.style.top = (y + 20) + 'px';
        normHinweis.hidden = false;
    }
    /** Text um einen Bildschirmpunkt im Editor (je 160 Zeichen davor und danach) und die Stelle darin. */
    function textAnPunkt(el, x, y) {
        let knoten, versatz;
        if (document.caretPositionFromPoint) {
            const p = document.caretPositionFromPoint(x, y);
            if (!p) return null;
            knoten = p.offsetNode; versatz = p.offset;
        } else if (document.caretRangeFromPoint) {
            const r = document.caretRangeFromPoint(x, y);
            if (!r) return null;
            knoten = r.startContainer; versatz = r.startOffset;
        } else return null;
        if (!imEditor(el, knoten)) return null;
        const vor = document.createRange();
        vor.selectNodeContents(el);
        vor.setEnd(knoten, versatz);
        const nach = document.createRange();
        nach.selectNodeContents(el);
        nach.setStart(knoten, versatz);
        const a = vor.toString(), b = nach.toString();
        const davor = a.substring(Math.max(0, a.length - 160));
        return { text: davor + b.substring(0, 160), stelle: davor.length };
    }

    // Graue Vorschau, wenn vor der Schreibmarke ein Kürzel steht (Tab fügt den Baustein ein).
    let hinweis = null;
    function hinweisVerbergen() { if (hinweis) hinweis.hidden = true; }
    // Autokorrektur nach dem Wortende bzw. nach einem diktierten Abschnitt; die Regeln (Liste, Ausnahmen, Diktat) prüft .NET.
    let letzteBerichtigung = null;
    async function tippfehler(el, art, daten) {
        if (!seite) return;
        if (art === 'historyUndo' && letzteBerichtigung) {
            // Strg+Z nach einer Berichtigung: Wort künftig in dieser Klausur nicht mehr berichtigen.
            const b = letzteBerichtigung;
            letzteBerichtigung = null;
            if (textVorCursor(el, 300).includes(b.alt) && !/\s/u.test(b.alt)) seite.invokeMethodAsync('AusnahmeHinzufuegen', b.alt);
            return;
        }
        if ((art !== 'insertText' && art !== 'insertReplacementText') || !daten) return;
        if (daten.length === 1 && !/^[\s.,;:!?)"“«»']$/u.test(daten)) return;
        const vor = textVorCursor(el, 200);
        const e = await seite.invokeMethodAsync('Autokorrektur', vor, Math.min(daten.length, vor.length));
        if (!e || textVorCursor(el, 200) !== vor) return; // inzwischen weitergetippt
        const s = document.getSelection();
        const dahinter = vor.length - e.start - e.laenge;
        for (let i = 0; i < dahinter; i++) s.modify('move', 'backward', 'character');
        for (let i = 0; i < e.laenge; i++) s.modify('extend', 'backward', 'character');
        document.execCommand('insertText', false, e.neu);
        for (let i = 0; i < dahinter; i++) s.modify('move', 'forward', 'character');
        letzteBerichtigung = { alt: e.alt, neu: e.neu };
    }

    function kuerzelHinweis(el) {
        const s = document.getSelection();
        const m = s && s.isCollapsed ? kuerzelVorCursor.exec(textVorCursor(el, 30)) : null;
        const text = m ? kuerzel[m[1].toLowerCase()] : undefined;
        if (!text) { hinweisVerbergen(); return; }
        if (!hinweis) {
            hinweis = document.createElement('div');
            hinweis.className = 'kuerzelvorschau';
            document.body.appendChild(hinweis);
        }
        hinweis.textContent = '→ ' + (text.length > 160 ? text.substring(0, 159) + '…' : text) + '   Tab';
        const r = s.getRangeAt(0).getBoundingClientRect();
        const rand = el.getBoundingClientRect();
        hinweis.style.left = Math.max(rand.left + 8, Math.min(r.left, rand.right - 320)) + 'px';
        hinweis.style.top = (r.bottom + 4 || rand.top + 24) + 'px';
        hinweis.style.maxWidth = Math.max(240, rand.right - rand.left - 32) + 'px';
        hinweis.hidden = false;
    }

    // ---------- Seitenleiste Sachverhalt/Lösung ----------
    const unterlage = {
        /** Fundstellen mit <mark> hervorheben; richtung 0 = neu suchen, ±1 = weiter. Rückgabe „i/n“ oder ''. */
        suchen(begriff, richtung) {
            const wurzel = document.querySelector('.unterlage');
            if (!wurzel) return '';
            if (richtung === 0) {
                for (const m of wurzel.querySelectorAll('mark.fund')) m.replaceWith(document.createTextNode(m.textContent));
                wurzel.normalize();
                const b = (begriff || '').trim();
                if (b.length < 2) return '';
                const muster = new RegExp(b.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi');
                const gang = document.createTreeWalker(wurzel, NodeFilter.SHOW_TEXT);
                const knoten = [];
                while (gang.nextNode()) knoten.push(gang.currentNode);
                for (const t of knoten) {
                    const text = t.nodeValue;
                    muster.lastIndex = 0;
                    if (!muster.test(text)) continue;
                    muster.lastIndex = 0;
                    const teil = document.createDocumentFragment();
                    let pos = 0, m;
                    while ((m = muster.exec(text)) !== null) {
                        teil.appendChild(document.createTextNode(text.substring(pos, m.index)));
                        const mark = document.createElement('mark');
                        mark.className = 'fund';
                        mark.textContent = m[0];
                        teil.appendChild(mark);
                        pos = m.index + m[0].length;
                    }
                    teil.appendChild(document.createTextNode(text.substring(pos)));
                    t.replaceWith(teil);
                }
                wurzel.dataset.fund = '-1';
            }
            const funde = wurzel.querySelectorAll('mark.fund');
            if (!funde.length) return (begriff || '').trim().length >= 2 ? 'keine Treffer' : '';
            let i = parseInt(wurzel.dataset.fund || '-1', 10);
            i = ((i + (richtung === 0 ? 1 : richtung)) % funde.length + funde.length) % funde.length;
            wurzel.dataset.fund = String(i);
            funde.forEach((f, k) => f.classList.toggle('aktuell', k === i));
            funde[i].scrollIntoView({ block: 'center' });
            return (i + 1) + '/' + funde.length;
        },
        /** Markierter Text innerhalb der Seitenleiste. */
        auswahl() {
            const s = document.getSelection();
            const wurzel = document.querySelector('.unterlage');
            if (!s || !s.rangeCount || !wurzel || !wurzel.contains(s.anchorNode)) return '';
            return s.toString().trim();
        },
        /** Gliederungsnavigation: Überschrift (ersatzweise Absatz) mit dem Begriff zeigen und kurz hervorheben. */
        ueberschriftZeigen(begriff) {
            const wurzel = document.querySelector('.unterlage');
            if (!wurzel) return false;
            const b = (begriff || '').toLowerCase();
            const passt = el => el.textContent.toLowerCase().includes(b);
            const ziel = [...wurzel.querySelectorAll('h1, h2, h3, h4, h5, h6')].find(passt)
                || [...wurzel.querySelectorAll('p, li')].find(passt);
            if (!ziel) return false;
            ziel.scrollIntoView({ block: 'start' });
            ziel.classList.add('angesprungen');
            setTimeout(() => ziel.classList.remove('angesprungen'), 2000);
            return true;
        },
        /** PDF (Bearbeitung/Erstvotum) als Blob-Adresse für das iframe der Leiste. */
        pdfAdresse(bytes) {
            return URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
        },
        adresseFreigeben(adresse) {
            if (adresse) URL.revokeObjectURL(adresse);
        },
        /** Gezeigte Norm in den Blick: hervorgehobene Stelle, sonst Überschrift, die mit dem Zitat beginnt. */
        normZeigen(zitat, nurUeberschrift) {
            const wurzel = document.querySelector('.unterlage');
            if (!wurzel) return;
            const text = el => el.textContent.replace(/\s+/g, ' ').trim();
            const ziel = (!nurUeberschrift && wurzel.querySelector('mark.hervor'))
                || [...wurzel.querySelectorAll('h3, h4')].find(h => text(h) === zitat || text(h).startsWith(zitat + ' '));
            if (ziel) ziel.scrollIntoView({ block: nurUeberschrift ? 'start' : 'center' });
        },
        /** Klick auf eine Überschrift → .NET (Sprung zur Rohpunktzeile); Klick auf ein Normzitat → Volltext. */
        anmelden(ref) {
            seite = ref;
            document.addEventListener('click', e => {
                const norm = e.target instanceof Element ? e.target.closest('.unterlage .norm') : null;
                if (norm) {
                    e.preventDefault();
                    ref.invokeMethodAsync('NormGewaehlt', norm.dataset.gesetz || '', norm.dataset.nr || '');
                    return;
                }
                const h = e.target instanceof Element ? e.target.closest('.unterlage h1, .unterlage h2, .unterlage h3, .unterlage h4, .unterlage h5, .unterlage h6') : null;
                if (!h || (document.getSelection() && !document.getSelection().isCollapsed)) return;
                ref.invokeMethodAsync('UeberschriftGewaehlt', h.textContent.replace(/\s+/g, ' ').trim());
            });
        },
    };

    /** Zeile im Reiter „Rohpunkte“ sichtbar machen und ihr Punktefeld fokussieren. */
    function rohpunktZeigen(id) {
        const el = document.getElementById(id);
        if (!el) return;
        el.scrollIntoView({ block: 'center' });
        const feld = el.matches('input') ? el : el.querySelector('input[type=number]');
        if (feld) { feld.focus(); if (feld.select) feld.select(); }
    }

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

    /** Verweise in Sachverhalt/Lösung über .NET öffnen (Mac: Standardbrowser statt des Programmfensters). */
    function verweiseAbfangen(ref) {
        document.addEventListener('click', e => {
            const a = e.target instanceof Element ? e.target.closest('.unterlage a[href]') : null;
            if (!a) return;
            e.preventDefault();
            ref.invokeMethodAsync('VerweisOeffnen', a.getAttribute('href'));
        });
    }

    return { speicher, herunterladen, fokus, editor, feld, beimVerlassen, neuLaden, verweiseAbfangen, unterlage, rohpunktZeigen };
})();
