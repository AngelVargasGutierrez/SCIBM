
        pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/2.16.105/pdf.worker.min.js';

        // Lista de alumnos matriculados en JS para la coincidencia difusa
        var matriculados = [];
        
        {
            foreach (var al in matriculados)
            {
                <text>
                matriculados.push({
                    id: '
                    nombreCompleto: 
                    apellidos: 
                    nombres: 
                });
                </text>
            }
        }

        // Estructura de plantilla del examen
        var plantillaPreguntas = [];
        
        {
            <text>
            plantillaPreguntas.push({
                numeroPregunta: 
                inciso: 
                pagina: 
                preguntaPadreId: '
                enunciado: 
                tipo: 
                respuestaCorrecta: 
                puntaje: 
                posX: 
                posY: 
                width: 25,
                height: 5,
                opcionesJson: 
            });
            </text>
        }

        // Archivos temporales subidos
        var tempFiles = [];
        
        {
            foreach (var tf in tempFiles)
            {
                <text>tempFiles.push(
            }
        }

        // Datos calificados (resultados en memoria)
        var resultadosAlumnos = [];
        var activeStudentIndex = -1;
        var aiResultsJson = 

        // Iniciar procesamiento al cargar
        window.addEventListener('load', function() {
            if (aiResultsJson) {
                // Modo Inteligencia Artificial (Saltamos Tesseract)
                document.getElementById("grader-loader").style.display = "none";
                resultadosAlumnos = aiResultsJson;
                
                // Aplicar fuzzy match a cada uno para asociarlo al id matriculado
                resultadosAlumnos.forEach(res => {
                    var matchedAlumno = fuzzyMatchAlumno(res.nombreAlumnoRaw);
                    res.alumnoMatriculadoId = matchedAlumno ? matchedAlumno.id : null;
                    
                    if (!res.tieneObservacion) {
                        res.tieneObservacion = matchedAlumno === null;
                        res.observacion = res.tieneObservacion ? "No se encontró coincidencia con ningún alumno matriculado" : "";
                    }
                });
                
                document.getElementById("scanned-count-indicator").innerText = `${resultadosAlumnos.length} Calificados (IA)`;
                renderScannedList();
                if (resultadosAlumnos.length > 0) {
                    selectStudent(0);
                }
            } else {
                // Modo Manual (OMR/OCR Local)
                procesarArchivosAlumnos();
            }
        });

        // 1. Escanear y calificar de forma secuencial en el navegador (OMR + OCR)
        async function procesarArchivosAlumnos() {
            var loader = document.getElementById("grader-loader");
            var loaderTitle = document.getElementById("loader-title");
            var loaderSubtitle = document.getElementById("loader-subtitle");
            var fill = document.getElementById("loader-progress-bar");

            loader.style.display = "flex";
            var total = tempFiles.length;
            
            // Inicializar worker de Tesseract
            loaderTitle.innerText = "Inicializando OCR...";
            loaderSubtitle.innerText = "Cargando diccionario de idioma español...";
            var ocrWorker = await Tesseract.createWorker();
            await ocrWorker.loadLanguage('spa');
            await ocrWorker.initialize('spa');

            for (var i = 0; i < total; i++) {
                var filePath = tempFiles[i];
                var fileName = filePath.substring(filePath.lastIndexOf('/') + 1);
                
                loaderTitle.innerText = `Calificando examen ${i + 1} de ${total}...`;
                loaderSubtitle.innerText = `Procesando: ${fileName}`;
                fill.style.width = ((i / total) * 100) + "%";

                try {
                    // Cargar PDF del alumno
                    var fileUrl = '
                    var pdf = await pdfjsLib.getDocument(fileUrl).promise;
                    var page = await pdf.getPage(1); // Escaneamos la primera página para OMR/OCR nombre
                    
                    // Renderizar página a escala estándar para OMR
                    var scale = 1.5;
                    var viewport = page.getViewport({ scale: scale });
                    var virtualCanvas = document.createElement("canvas");
                    virtualCanvas.width = viewport.width;
                    virtualCanvas.height = viewport.height;
                    var vCtx = virtualCanvas.getContext("2d");
                    
                    await page.render({ canvasContext: vCtx, viewport: viewport }).promise;

                    // A. AUTO-ALINEACIÓN (Delta X, Delta Y)
                    // Buscamos la posición del texto de la primera pregunta para ajustar desfases
                    // Para simplificar y hacerlo ultra-rápido, asumiremos alineación directa.
                    // No obstante, si se desalinea, permitiremos la revisión y cambio manual en la grilla.
                    
                    // B. OCR DEL NOMBRE DEL ALUMNO (Parte superior del examen)
                    // Asumiremos que el área del nombre está en el primer 15% superior de la hoja
                    var nameCanvas = document.createElement("canvas");
                    nameCanvas.width = viewport.width;
                    nameCanvas.height = viewport.height * 0.15;
                    var nameCtx = nameCanvas.getContext("2d");
                    nameCtx.drawImage(virtualCanvas, 0, 0, viewport.width, viewport.height * 0.15, 0, 0, viewport.width, viewport.height * 0.15);
                    
                    var nameOcr = await ocrWorker.recognize(nameCanvas.toDataURL());
                    var rawNameText = nameOcr.data.text.trim();
                    
                    // Limpiar saltos de línea y basura del OCR
                    rawNameText = rawNameText.replace(/[\r\n\t]+/g, ' ').replace(/[^a-zA-ZáéíóúÁÉÍÓÚñÑ\s,]/g, '').trim();
                    if (rawNameText.length < 3) rawNameText = "Ilegible / Sin Nombre";

                    // C. COINCIDENCIA DIFUSA CONTRA MATRICULADOS (Matching 2-3 palabras del nombre)
                    var matchedAlumno = fuzzyMatchAlumno(rawNameText);
                    var alumnoId = matchedAlumno ? matchedAlumno.id : null;
                    var tieneObservacion = matchedAlumno === null;
                    var observacion = tieneObservacion ? "No se encontró coincidencia con ningún alumno matriculado" : "";

                    // D. LECTURA DE RESPUESTAS (OMR de círculos marcados / OCR de campos)
                    var respuestasAlumno = [];
                    for (var q of plantillaPreguntas) {
                        var respuestaDetectada = "";

                        if (q.tipo === "OpcionMultiple" && q.opcionesJson) {
                            var opciones = JSON.parse(q.opcionesJson);
                            var maxDensity = -1;
                            var selectedOption = "A"; // Default fallback

                            // Evaluar la densidad de píxeles (OMR) en cada opción de la pregunta
                            for (var opt of opciones) {
                                // Mapear porcentaje a píxeles del canvas
                                var ox = viewport.width * (opt.x / 100.0);
                                var oy = viewport.height * (opt.y / 100.0);
                                var ow = viewport.width * (opt.w / 100.0);
                                var oh = viewport.height * (opt.h / 100.0);

                                if (ow <= 0) ow = 12;
                                if (oh <= 0) oh = 12;

                                // Extraer datos de píxeles
                                var imgData = vCtx.getImageData(ox, oy, ow, oh);
                                var pixels = imgData.data;
                                var darkPixels = 0;

                                // Contar cuántos píxeles son oscuros (lapicero/marca)
                                for (var pIdx = 0; pIdx < pixels.length; pIdx += 4) {
                                    var r = pixels[pIdx];
                                    var g = pixels[pIdx+1];
                                    var b = pixels[pIdx+2];
                                    var gray = 0.299*r + 0.587*g + 0.114*b;
                                    
                                    if (gray < 130) { // Umbral de oscuridad (0-255)
                                        darkPixels++;
                                    }
                                }

                                var density = darkPixels / (ow * oh);
                                if (density > maxDensity) {
                                    maxDensity = density;
                                    selectedOption = opt.label;
                                }
                            }
                            
                            // Si la densidad máxima es muy baja (ej: < 5%), asumimos que no marcó ninguna
                            respuestaDetectada = maxDensity > 0.05 ? selectedOption : "";
                        } 
                        else if (q.tipo === "RespuestaLibre") {
                            // Hacer OCR localizado en la caja del espacio vacío
                            var qx = viewport.width * (q.posX / 100.0);
                            var qy = viewport.height * ((q.posY + q.height) / 100.0); // Abajo
                            var qw = viewport.width * (Math.min(25, q.width) / 100.0);
                            var qh = viewport.height * (3.2 / 100.0);

                            var cropCanvas = document.createElement("canvas");
                            cropCanvas.width = qw;
                            cropCanvas.height = qh;
                            var cropCtx = cropCanvas.getContext("2d");
                            cropCtx.drawImage(virtualCanvas, qx, qy, qw, qh, 0, 0, qw, qh);

                            var cropOcr = await ocrWorker.recognize(cropCanvas.toDataURL());
                            respuestaDetectada = cropOcr.data.text.trim().replace(/[\r\n\t]+/g, ' ');
                        }

                        respuestasAlumno.push({
                            numeroPregunta: q.numeroPregunta,
                            inciso: q.inciso || null,
                            respuestaDada: respuestaDetectada
                        });
                    }

                    resultadosAlumnos.push({
                        tempFilePath: filePath,
                        fileName: fileName,
                        nombreAlumnoRaw: rawNameText,
                        alumnoMatriculadoId: alumnoId,
                        tieneObservacion: tieneObservacion,
                        observacion: observacion,
                        respuestas: respuestasAlumno
                    });

                } catch (err) {
                    console.error("Error al calificar archivo: ", err);
                    resultadosAlumnos.push({
                        tempFilePath: filePath,
                        fileName: fileName,
                        nombreAlumnoRaw: "Error de lectura / PDF corrupto",
                        alumnoMatriculadoId: null,
                        tieneObservacion: true,
                        observacion: "El PDF no se pudo renderizar o está corrupto: " + err.message,
                        respuestas: []
                    });
                }
            }

            await ocrWorker.terminate();
            fill.style.width = "100%";
            loader.style.display = "none";

            // Mostrar el panel de revisión
            document.getElementById("scanned-count-indicator").innerText = `${resultadosAlumnos.length} Calificados`;
            renderScannedList();
            if (resultadosAlumnos.length > 0) {
                selectStudent(0);
            }
        }

        // Algoritmo de coincidencia difusa (Jaccard de palabras)
        function fuzzyMatchAlumno(ocrName) {
            if (!ocrName || ocrName.length < 4) return null;
            
            // Separar el nombre del OCR en palabras clave (ignorando artículos cortos)
            var ocrWords = ocrName.toUpperCase().split(/[\s,]+/).filter(w => w.length > 2);

            if (ocrWords.length === 0) return null;

            var bestMatch = null;
            var maxMatchCount = 0;

            for (var al of matriculados) {
                var nameUpper = al.nombreCompleto.toUpperCase();
                var matchCount = 0;

                // Contar cuántas palabras del OCR están en el nombre completo del alumno matriculado
                ocrWords.forEach(w => {
                    if (nameUpper.includes(w)) {
                        matchCount++;
                    }
                });

                // Si coinciden al menos 2 palabras (o todas si son pocas)
                if (matchCount >= 2 && matchCount > maxMatchCount) {
                    maxMatchCount = matchCount;
                    bestMatch = al;
                }
            }

            return bestMatch;
        }

        // 2. Renderizar lista de alumnos escaneados a la izquierda
        function renderScannedList() {
            var list = document.getElementById("students-scanned-list-el");
            list.innerHTML = "";

            resultadosAlumnos.forEach((res, idx) => {
                var item = document.createElement("div");
                item.className = "student-scan-item" + (activeStudentIndex === idx ? " active" : "") + (res.tieneObservacion ? " error" : "");
                
                // Mapear nombre matriculado para mostrar en la lista
                var displayName = "No Matriculado / Observado";
                if (res.alumnoMatriculadoId) {
                    var al = matriculados.find(a => a.id === res.alumnoMatriculadoId);
                    if (al) displayName = al.nombreCompleto;
                }

                // Calcular nota preliminar en vivo
                var score = calcularNotaPreliminar(res);

                var statusIcon = res.sellado ? `<i class="fa-solid fa-circle-check" style="color: #2ec4b6;" title="Revisado y Sellado"></i>` : `<i class="fa-solid fa-triangle-exclamation" style="color: #ff9f1c;" title="Falta Revisar"></i>`;

                var actionsHtml = "";
                if (res.tieneObservacion || !res.alumnoMatriculadoId) {
                    actionsHtml += `<button onclick="event.stopPropagation(); matricularAlVuelo(${idx})" style="background:transparent; border:none; color:#2ec4b6; cursor:pointer; padding:2px 5px;" title="Matricular usando el nombre leído por IA"><i class="fa-solid fa-plus"></i></button>`;
                }
                actionsHtml += `<button onclick="event.stopPropagation(); eliminarExamen(${idx})" style="background:transparent; border:none; color:#ff5252; cursor:pointer; padding:2px 5px;" title="Eliminar examen del lote"><i class="fa-solid fa-minus"></i></button>`;

                item.innerHTML = `
                    <div class="item-row">
                        <div style="display:flex; align-items:center; gap:8px;">
                            ${statusIcon}
                            <strong style="font-size:14px; color:${res.tieneObservacion ? '#ff5252' : '#ffffff'};">${displayName}</strong>
                        </div>
                        <span class="grade-badge" style="font-size:15px;">${score.toFixed(1)}</span>
                    </div>
                    <div style="display:flex; justify-content:space-between; align-items:flex-end; margin-top: 4px; gap:8px;">
                        <div style="flex:1; min-width:0; font-size: 11px; color: var(--text-muted); opacity: 0.8;">
                            <div style="overflow:hidden; text-overflow:ellipsis; white-space:nowrap;">PDF: ${res.fileName}</div>
                            <div style="font-style: italic; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;">IA: "${res.nombreAlumnoRaw}"</div>
                            ${res.tieneObservacion ? `<div style="color:#ff5252; margin-top:2px; font-weight:normal; line-height:1.2;">Detalle: ${res.observacion}</div>` : ''}
                        </div>
                        <div style="display:flex; gap:5px; flex-shrink:0; align-items:center; background: rgba(0,0,0,0.3); padding: 4px; border-radius: 4px; margin-bottom:auto;">
                            ${actionsHtml}
                        </div>
                    </div>
                `;

                item.onclick = function() {
                    selectStudent(idx);
                };

                list.appendChild(item);
            });
        }

        function normalizarTexto(texto) {
            return (texto || "")
                .toUpperCase()
                .normalize("NFD").replace(/[\u0300-\u036f]/g, "") // Quitar tildes
                .replace(/[^A-Z0-9]/g, "") // Quitar símbolos, puntos, comas, espacios
                .trim();
        }

        // Calcular la nota sumando los puntajes de aciertos
        function calcularNotaPreliminar(res) {
            var total = 0;
            res.respuestas.forEach(resp => {
                var q = plantillaPreguntas.find(p =>
                    p.numeroPregunta === resp.numeroPregunta &&
                    (resp.inciso ? p.inciso === resp.inciso : !p.inciso)
                );
                if (q && q.puntaje > 0) {
                    var corrNorm = normalizarTexto(q.respuestaCorrecta || "");
                    var dadaNorm = normalizarTexto(resp.respuestaDada || "");
                    
                    var aiCorrecto = (corrNorm === dadaNorm);
                    
                    // Flexibilidad Humana: si la respuesta es larga, permitimos que una incluya a la otra
                    if (!aiCorrecto && corrNorm.length >= 4 && dadaNorm.length >= 4) {
                        if (corrNorm.includes(dadaNorm) || dadaNorm.includes(corrNorm)) {
                            aiCorrecto = true;
                        }
                    }

                    var esCorrecto = resp.esCorrectoManual !== undefined && resp.esCorrectoManual !== null 
                                     ? resp.esCorrectoManual 
                                     : aiCorrecto;

                    if (esCorrecto) total += q.puntaje;
                }
            });
            return total;
        }

        // 3. Seleccionar alumno de la lista e instanciar su desglose y vista previa PDF
        function selectStudent(idx) {
            activeStudentIndex = idx;
            document.getElementById("breakdown-panel-el").style.display = "flex";

            // Marcar activo en la lista
            var items = document.querySelectorAll(".student-scan-item");
            items.forEach((item, i) => {
                if (i === idx) item.classList.add("active");
                else item.classList.remove("active");
            });

            var res = resultadosAlumnos[idx];

            // 1. Cargar el selector de matrícula con su valor
            document.getElementById("select-matricula-al").value = res.alumnoMatriculadoId || "";

            // 2. Renderizar respuestas
            renderAnswersVerifyList(res);

            // 3. Mostrar observaciones si existen
            var warning = document.getElementById("warning-match-el");
            warning.style.display = res.tieneObservacion ? "block" : "none";

            // 4. Renderizar PDF del alumno en la vista previa
            var tempName = res.tempFilePath ? res.tempFilePath.split('/').pop() : encodeURIComponent(res.fileName);
            var fileUrl = '
        function renderAnswersVerifyList(res) {
            var container = document.getElementById("answers-verify-list-el");
            container.innerHTML = "";

            res.respuestas.forEach((resp, rIdx) => {
                var q = plantillaPreguntas.find(p =>
                    p.numeroPregunta === resp.numeroPregunta &&
                    (resp.inciso ? p.inciso === resp.inciso : !p.inciso)
                );
                if (!q || q.puntaje === 0) return; // Saltar contenedores padre o no encontrados

                var corrNorm = normalizarTexto(q.respuestaCorrecta || "");
                var dadaNorm = normalizarTexto(resp.respuestaDada || "");
                
                var aiCorrecto = (corrNorm === dadaNorm);
                
                // Flexibilidad Humana: si la respuesta es larga, permitimos que una incluya a la otra
                if (!aiCorrecto && corrNorm.length >= 4 && dadaNorm.length >= 4) {
                    if (corrNorm.includes(dadaNorm) || dadaNorm.includes(corrNorm)) {
                        aiCorrecto = true;
                    }
                }

                var esCorrecto = resp.esCorrectoManual !== undefined && resp.esCorrectoManual !== null 
                                 ? resp.esCorrectoManual 
                                 : aiCorrecto;
                
                var item = document.createElement("div");
                item.className = "answer-verify-item " + (esCorrecto ? "correct" : "incorrect");

                var optHtml = "";
                var isManual = esCorrecto ? "checked" : "";

                if (q.tipo === "OpcionMultiple" && q.opcionesJson) {
                    try {
                        var opcionesArr = JSON.parse(q.opcionesJson);
                        var options = `<option value="">--</option>`;
                        opcionesArr.forEach(o => {
                            var sel = (resp.respuestaDada === o.label) ? "selected" : "";
                            options += `<option value="${o.label}" ${sel}>${o.label}</option>`;
                        });

                        optHtml = `<select class="form-input" style="width:60px; padding:2px; font-size:13px;" onchange="cambiarRespuestaAlumno(${rIdx}, this.value)">
                                  ${options}
                               </select>
                               <label style="font-size:11px; display:flex; align-items:center; gap:3px; margin-left:5px; cursor:pointer;" title="Forzar Correcto/Incorrecto">
                                   <input type="checkbox" ${isManual} onchange="toggleCorrectoManual(${rIdx}, this.checked)" /> Ok
                               </label>`;
                    } catch(e) {}
                }
                
                if (!optHtml) {
                    var isCheckOrSelect = q.tipo === "OpcionMultiple" || q.tipo === "VerdaderoFalso" || (q.respuestaCorrecta && (q.respuestaCorrecta.toUpperCase() === "V" || q.respuestaCorrecta.toUpperCase() === "F"));
                    if (isCheckOrSelect) {
                        var labels = ["A", "B", "C", "D", "E"];
                        if (q.tipo === "VerdaderoFalso" || (q.respuestaCorrecta && (q.respuestaCorrecta.toUpperCase() === "V" || q.respuestaCorrecta.toUpperCase() === "F"))) {
                            labels = ["V", "F"];
                        }
                        var options = `<option value="" ${!resp.respuestaDada ? "selected" : ""}>--</option>`;
                        labels.forEach(l => {
                            options += `<option value="${l}" ${(resp.respuestaDada || "").toUpperCase() === l ? "selected" : ""}>${l}</option>`;
                        });
                        optHtml = `<select class="form-input" style="width:60px; padding:2px; font-size:13px;" onchange="cambiarRespuestaAlumno(${rIdx}, this.value)">
                                      ${options}
                                   </select>
                                   <label style="font-size:11px; display:flex; align-items:center; gap:3px; margin-left:5px; cursor:pointer;" title="Forzar Correcto/Incorrecto">
                                       <input type="checkbox" ${isManual} onchange="toggleCorrectoManual(${rIdx}, this.checked)" /> Ok
                                   </label>`;
                    } else {
                        optHtml = `<input type="text" class="form-input" style="width:110px; padding:4px 8px; font-size:13px;" value="${resp.respuestaDada}" oninput="cambiarRespuestaAlumno(${rIdx}, this.value)" />
                                   <label style="font-size:11px; display:flex; align-items:center; gap:3px; margin-left:5px; cursor:pointer;" title="Forzar Correcto/Incorrecto">
                                       <input type="checkbox" ${isManual} onchange="toggleCorrectoManual(${rIdx}, this.checked)" /> Ok
                                   </label>`;
                    }
                }

                var label = `Pregunta ${q.numeroPregunta}`;
                if (q.inciso) label += ` — Inciso ${q.inciso}`;

                item.innerHTML = `
                    <div>
                        <div style="font-weight:600; font-size:13.5px;">${label}</div>
                        <div style="font-size:12px; color:var(--text-muted);">
                            Correcta: <strong style="color:#2ec4b6;">${q.respuestaCorrecta}</strong> | Peso: ${q.puntaje.toFixed(1)} pt
                        </div>
                    </div>
                    <div style="display:flex; align-items:center; gap:10px;">
                        ${optHtml}
                        <span style="font-size:18px; color:${esCorrecto ? '#2ec4b6' : '#ff5252'};">
                            <i class="fa-solid ${esCorrecto ? 'fa-circle-check' : 'fa-circle-xmark'}"></i>
                        </span>
                    </div>
                `;

                container.appendChild(item);
            });

            // Actualizar nota en pantalla
            var score = calcularNotaPreliminar(res);
            document.getElementById("breakdown-grade").innerText = score.toFixed(1);
        }

        // Cambiar respuesta de alumno de forma manual
        function cambiarRespuestaAlumno(rIdx, value) {
            var res = resultadosAlumnos[activeStudentIndex];
            res.respuestas[rIdx].respuestaDada = value;
            
            // Recalcular y re-renderizar
            renderAnswersVerifyList(res);
            renderScannedList();
        }

        // Forzar pregunta como correcta (para respuestas libres)
        function toggleCorrectoManual(rIdx, isChecked) {
            var res = resultadosAlumnos[activeStudentIndex];
            res.respuestas[rIdx].esCorrectoManual = isChecked;
            
            // Recalcular la nota preliminar (borramos notaFinal para forzar recalculo)
            res.notaFinal = undefined;
            res.notaPreliminar = calcularNotaPreliminar(res);
            
            renderAnswersVerifyList(res);
            renderScannedList();
            renderPdfOverlay(document.getElementById("preview-canvas").width, document.getElementById("preview-canvas").height);
        }

        // Asociar alumno matriculado desde el dropdown
        function asociarAlumnoMatriculado(val) {
            var res = resultadosAlumnos[activeStudentIndex];
            res.alumnoMatriculadoId = val ? val : null;
            res.tieneObservacion = (val === "");
            res.observacion = res.tieneObservacion ? "No se encontró coincidencia con ningún alumno matriculado" : "";

            document.getElementById("warning-match-el").style.display = res.tieneObservacion ? "block" : "none";
            renderScannedList();
        }

        // Renderizar vista previa del PDF
        var previewPdfDoc = null;
        var currentPdfPage = 1;
        var totalPdfPages = 1;
        var currentPdfScale = 0.65;

        function renderPdfPreview(url) {
            var pCanvas = document.getElementById("preview-canvas");
            var pCtx = pCanvas.getContext("2d");
            currentPdfPage = 1;

            pdfjsLib.getDocument(url).promise.then(function(pdf) {
                previewPdfDoc = pdf;
                totalPdfPages = pdf.numPages;
                document.getElementById("pdf-page-indicator").innerText = "Pág " + currentPdfPage + "/" + totalPdfPages;
                
                pdf.getPage(currentPdfPage).then(function(page) {
                    var viewport = page.getViewport({ scale: currentPdfScale });
                    pCanvas.width = viewport.width;
                    pCanvas.height = viewport.height;

                    page.render({
                        canvasContext: pCtx,
                        viewport: viewport
                    }).promise.then(function() {
                        renderPdfOverlay(viewport.width, viewport.height);
                    });
                });
            });
        }

        function changePdfPage(delta) {
            if (!previewPdfDoc) return;
            var newPage = currentPdfPage + delta;
            if (newPage >= 1 && newPage <= totalPdfPages) {
                currentPdfPage = newPage;
                document.getElementById("pdf-page-indicator").innerText = "Pág " + currentPdfPage + "/" + totalPdfPages;
                
                var pCanvas = document.getElementById("preview-canvas");
                var pCtx = pCanvas.getContext("2d");
                
                // Si cambiamos de página salimos del modo sello
                if (selloModoActivo) confirmarExamenActual();
                
                previewPdfDoc.getPage(currentPdfPage).then(function(page) {
                    var viewport = page.getViewport({ scale: currentPdfScale });
                    pCanvas.width = viewport.width;
                    pCanvas.height = viewport.height;

                    page.render({
                        canvasContext: pCtx,
                        viewport: viewport
                    }).promise.then(function() {
                        renderPdfOverlay(viewport.width, viewport.height);
                    });
                });
            }
        }

        function changePdfScale(delta) {
            if (!previewPdfDoc) return;
            currentPdfScale += delta;
            if (currentPdfScale < 0.2) currentPdfScale = 0.2;
            if (currentPdfScale > 3.0) currentPdfScale = 3.0;

            var pCanvas = document.getElementById("preview-canvas");
            var pCtx = pCanvas.getContext("2d");
            
            previewPdfDoc.getPage(currentPdfPage).then(function(page) {
                var viewport = page.getViewport({ scale: currentPdfScale });
                pCanvas.width = viewport.width;
                pCanvas.height = viewport.height;

                page.render({
                    canvasContext: pCtx,
                    viewport: viewport
                }).promise.then(function() {
                    renderPdfOverlay(viewport.width, viewport.height);
                });
            });
        }

        // ============================================
        // 5. FLUJO DE SELLADO INTERACTIVO
        // ============================================
        var selloModoActivo = false;
        var selloGlobal = { x: 
        if (selloGlobal.w === 0) selloGlobal = { x: 50, y: 50, w: 20, h: 10 }; // Default if empty

        function toggleSelloModo() {
            var btnColocar = document.getElementById("btn-colocar-sello");
            var btnGuardar = document.getElementById("btn-guardar-examen");
            var canvasWrap = document.getElementById("canvas-wrapper");
            
            selloModoActivo = true;
            btnColocar.style.display = "none";
            btnGuardar.style.display = "inline-block";
            canvasWrap.style.cursor = "crosshair"; // Simular cursor de sello
            
            // Evento para estampar al hacer clic en el canvas
            canvasWrap.onmousedown = function(e) {
                if (selloModoActivo && e.target.id === "preview-canvas") {
                    var rect = canvasWrap.getBoundingClientRect();
                    var xPos = ((e.clientX - rect.left) / rect.width) * 100;
                    var yPos = ((e.clientY - rect.top) / rect.height) * 100;
                    
                    var res = resultadosAlumnos[activeStudentIndex];
                    if (!res.stamp) res.stamp = { ...selloGlobal };
                    res.stamp.x = xPos;
                    res.stamp.y = yPos;
                    
                    selloModoActivo = false;
                    canvasWrap.style.cursor = "default";
                    canvasWrap.onmousedown = null; // Quitar evento
                    
                    renderPdfOverlay(document.getElementById("preview-canvas").width, document.getElementById("preview-canvas").height);
                }
            };
        }

        function confirmarExamenActual() {
            var res = resultadosAlumnos[activeStudentIndex];
            res.sellado = true; // Marca como revisado
            
            var btnColocar = document.getElementById("btn-colocar-sello");
            var btnGuardar = document.getElementById("btn-guardar-examen");
            var canvasWrap = document.getElementById("canvas-wrapper");
            
            selloModoActivo = false;
            canvasWrap.style.cursor = "default";
            canvasWrap.onmousedown = null;
            btnColocar.style.display = "inline-block";
            btnGuardar.style.display = "none";
            
            if (confirm("Se ha guardado la posición de los sellos. ¿Pasar al siguiente examen?")) {
                if (activeStudentIndex < resultadosAlumnos.length - 1) {
                    selectStudent(activeStudentIndex + 1);
                }
            } else {
                renderScannedList();
            }
        }

        function renderPdfOverlay(w, h) {
            var overlay = document.getElementById("pdf-overlay");
            overlay.innerHTML = ""; // Limpiar
            
            // Ocultar botón de sello si no estamos en la página 1
            if (currentPdfPage !== 1) {
                document.getElementById("btn-colocar-sello").style.display = "none";
            } else if (!selloModoActivo) {
                document.getElementById("btn-colocar-sello").style.display = "inline-block";
            }
            overlay.style.pointerEvents = "none"; // CRÍTICO: Debe ser none para dejar pasar el clic al canvas
            var res = resultadosAlumnos[activeStudentIndex];
            
            // 1. Dibujar Marks (Checks/X) de las preguntas
            if (res.respuestas) {
                res.respuestas.forEach((r, idx) => {
                    var q = plantillaPreguntas.find(p =>
                        p.numeroPregunta === r.numeroPregunta &&
                        (r.inciso ? p.inciso === r.inciso : !p.inciso)
                    );
                    
                    if (!q || q.puntaje === 0) return; // No es calificable o es contenedor
                    if (q.pagina !== currentPdfPage) return; // Solo dibujar en la página actual

                    var isCorrect = r.esCorrectoManual !== undefined && r.esCorrectoManual !== null 
                                    ? r.esCorrectoManual 
                                    : ((r.respuestaDada || "").toUpperCase() === (q.respuestaCorrecta || "").toUpperCase());
                    
                    var mark = document.createElement("div");
                    mark.style.position = "absolute";
                    mark.style.left = (q.posX) + "%";
                    mark.style.top = (q.posY) + "%";
                    mark.style.fontSize = "12px";
                    mark.style.fontWeight = "bold";
                    mark.style.cursor = "move";
                    mark.style.textShadow = "0px 0px 3px white";
                    mark.style.pointerEvents = "auto"; // Habilitar clics solo en el elemento
                    
                    if (isCorrect) {
                        mark.style.color = "#2ec4b6";
                        mark.innerHTML = `<i class="fa-solid fa-check"></i>`;
                    } else {
                        mark.style.color = "#ff5252";
                        mark.innerHTML = `<i class="fa-solid fa-xmark"></i> <span style="font-size:9px;">Cor: ${q.respuestaCorrecta}</span>`;
                    }
                    
                    makeDraggable(mark, null, function(newX, newY) {
                        // Guardar la nueva posición si se mueve
                        if(!res.correctionStamps) res.correctionStamps = {};
                        res.correctionStamps[idx] = { x: (newX/w)*100, y: (newY/h)*100 };
                    });
                    
                    // Restaurar pos custom si existe
                    if(res.correctionStamps && res.correctionStamps[idx]) {
                        mark.style.left = res.correctionStamps[idx].x + "%";
                        mark.style.top = res.correctionStamps[idx].y + "%";
                    }

                    overlay.appendChild(mark);
                });
            }

            // 2. Dibujar Sello de Nota Final (SOLO EN PÁGINA 1)
            if (currentPdfPage !== 1) return;

            if (!res.stamp) res.stamp = { ...selloGlobal };
            
            var stampDiv = document.createElement("div");
            stampDiv.style.position = "absolute";
            stampDiv.style.left = res.stamp.x + "%";
            stampDiv.style.top = res.stamp.y + "%";
            stampDiv.style.width = res.stamp.w + "%";
            stampDiv.style.height = res.stamp.h + "%";
            stampDiv.style.border = "2px dashed #ff9f1c";
            stampDiv.style.backgroundColor = "rgba(255, 159, 28, 0.2)";
            stampDiv.style.color = "#ff9f1c";
            stampDiv.style.display = "flex";
            stampDiv.style.alignItems = "center";
            stampDiv.style.justifyContent = "center";
            stampDiv.style.fontWeight = "bold";
            stampDiv.style.fontSize = "18px";
            stampDiv.style.cursor = "move";
            stampDiv.style.overflow = "hidden";
            stampDiv.style.pointerEvents = "auto"; // Habilitar arrastre solo en la caja
            
            var resizeHandle = document.createElement("div");
            resizeHandle.style.position = "absolute";
            resizeHandle.style.right = "0";
            resizeHandle.style.bottom = "0";
            resizeHandle.style.width = "15px";
            resizeHandle.style.height = "15px";
            resizeHandle.style.cursor = "se-resize";
            resizeHandle.style.backgroundColor = "transparent";
            var score = calcularNotaPreliminar(res);
            stampDiv.innerText = score.toFixed(1);
            stampDiv.appendChild(resizeHandle);

            makeDraggable(stampDiv, resizeHandle, function(newX, newY, newW, newH) {
                res.stamp.x = (newX / w) * 100;
                res.stamp.y = (newY / h) * 100;
                if (newW) res.stamp.w = (newW / w) * 100;
                if (newH) res.stamp.h = (newH / h) * 100;
                selloGlobal = { ...res.stamp }; // Actualizar memoria global para el siguiente alumno
            });

            overlay.appendChild(stampDiv);
        }

        function makeDraggable(elmnt, handle, onUpdate) {
            var pos1 = 0, pos2 = 0, pos3 = 0, pos4 = 0;
            var isResizing = false;
            
            if (handle) {
                handle.onmousedown = function(e) {
                    isResizing = true;
                    e.stopPropagation();
                    document.onmouseup = closeDragElement;
                    document.onmousemove = elementResize;
                };
            }

            elmnt.onmousedown = dragMouseDown;

            function dragMouseDown(e) {
                if (e.target === handle) return;
                e = e || window.event;
                e.preventDefault();
                isResizing = false;
                pos3 = e.clientX;
                pos4 = e.clientY;
                document.onmouseup = closeDragElement;
                document.onmousemove = elementDrag;
            }

            function elementDrag(e) {
                if (isResizing) return;
                e = e || window.event;
                e.preventDefault();
                pos1 = pos3 - e.clientX;
                pos2 = pos4 - e.clientY;
                pos3 = e.clientX;
                pos4 = e.clientY;
                elmnt.style.top = (elmnt.offsetTop - pos2) + "px";
                elmnt.style.left = (elmnt.offsetLeft - pos1) + "px";
            }
            
            function elementResize(e) {
                e = e || window.event;
                e.preventDefault();
                var w = e.clientX - elmnt.getBoundingClientRect().left;
                var h = e.clientY - elmnt.getBoundingClientRect().top;
                if(w < 50) w = 50;
                if(h < 20) h = 20;
                elmnt.style.width = w + "px";
                elmnt.style.height = h + "px";
            }

            function closeDragElement() {
                document.onmouseup = null;
                document.onmousemove = null;
                if (onUpdate) onUpdate(elmnt.offsetLeft, elmnt.offsetTop, elmnt.offsetWidth, elmnt.offsetHeight);
            }
        }

        // 4. Funciones de Accion Rápida (Matricular y Eliminar)
        let activeMatriculaIdx = -1;

        function showCustomAlert(title, message, iconColor = "#ff5252", iconClass = "fa-triangle-exclamation") {
            document.getElementById("custom-alert-title").innerText = title;
            document.getElementById("custom-alert-message").innerText = message;
            document.getElementById("custom-alert-icon").className = `fa-solid ${iconClass}`;
            document.getElementById("custom-alert-icon").style.color = iconColor;
            document.getElementById("custom-alert-modal").style.display = "flex";
        }
        function closeCustomAlert() {
            document.getElementById("custom-alert-modal").style.display = "none";
        }

        function closeMatricularConfirm() {
            activeMatriculaIdx = -1;
            document.getElementById("matricular-confirm-modal").style.display = "none";
        }

        function matricularAlVuelo(idx) {
            var res = resultadosAlumnos[idx];
            var nombreLeido = res.nombreAlumnoRaw;
            if (nombreLeido === "Ilegible / Sin Nombre" || nombreLeido === "Desconocido") {
                showCustomAlert("Nombre Inválido", "La IA no pudo leer un nombre válido. Escríbalo manualmente o intente de nuevo.");
                return;
            }

            activeMatriculaIdx = idx;
            document.getElementById("matricular-confirm-message").innerText = `¿Deseas registrar a "${nombreLeido}" como nuevo alumno matriculado en este curso?`;
            document.getElementById("matricular-confirm-modal").style.display = "flex";
        }

        function proceedMatricularAlVuelo() {
            if (activeMatriculaIdx === -1) return;
            var idx = activeMatriculaIdx;
            var res = resultadosAlumnos[idx];
            var nombreLeido = res.nombreAlumnoRaw;
            
            closeMatricularConfirm();

            var formData = new URLSearchParams();
            formData.append('examenId', '
            formData.append('nombreCompleto', nombreLeido);

            fetch('
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: formData
            })
            .then(r => r.json())
            .then(data => {
                if (data.success) {
                    // Agregarlo a la lista de matriculados en memoria
                    matriculados.push({ id: data.alumnoId, nombreCompleto: data.nombreCompleto });
                    
                    // Asociar al PDF actual
                    res.alumnoMatriculadoId = data.alumnoId;
                    res.tieneObservacion = false;
                    res.observacion = "";
                    
                    // Actualizar dropdown si es el alumno seleccionado activo
                    if (activeStudentIndex === idx) {
                        var sel = document.getElementById("select-matricula-al");
                        if(sel) {
                            var opt = document.createElement('option');
                            opt.value = data.alumnoId;
                            opt.innerHTML = data.nombreCompleto;
                            sel.appendChild(opt);
                            sel.value = data.alumnoId;
                        }
                        var warn = document.getElementById("warning-match-el");
                        if(warn) warn.style.display = "none";
                    }

                    renderScannedList();
                    showCustomAlert("¡Éxito!", "Alumno registrado y asociado correctamente.", "#2ec4b6", "fa-circle-check");
                } else {
                    showCustomAlert("Error al Registrar", data.message || "Error al registrar alumno");
                }
            })
            .catch(e => showCustomAlert("Error de red", "No se pudo conectar con el servidor."));
        }

        function eliminarExamen(idx) {
            if (!confirm("¿Seguro que deseas descartar este examen escaneado de la lista? No se guardará.")) return;
            
            // Eliminar elemento del array
            resultadosAlumnos.splice(idx, 1);
            
            if (resultadosAlumnos.length === 0) {
                document.getElementById("breakdown-panel-el").style.display = "none";
            } else {
                if (activeStudentIndex === idx) {
                    // Seleccionar el primero si borramos el actual
                    selectStudent(0);
                } else if (activeStudentIndex > idx) {
                    activeStudentIndex--; // Ajustar índice activo
                }
            }
            renderScannedList();
        }

        // Marcar como sellado/verificado y pasar al siguiente
        function guardarYSiguiente() {
            var res = resultadosAlumnos[activeStudentIndex];
            res.sellado = true; // Marcar como Revisado y Sellado
            
            if (activeStudentIndex < resultadosAlumnos.length - 1) {
                selectStudent(activeStudentIndex + 1);
            } else {
                showCustomAlert("Lote Revisado", "Has llegado al final de la lista de escaneos. Presiona 'Guardar Restantes' para finalizar y subir todo.", "#2ec4b6", "fa-check-double");
            }
            renderScannedList();
        }

        // 5. Guardar notas definitivas en el servidor vía AJAX
        function guardarNotasFinales() {
            var loader = document.getElementById("grader-loader");
            var loaderTitle = document.getElementById("loader-title");
            var loaderSubtitle = document.getElementById("loader-subtitle");
            var fill = document.getElementById("loader-progress-bar");

            loaderTitle.innerText = "Guardando calificaciones...";
            loaderSubtitle.innerText = "Estampando notas y actualizando SQL Server...";
            fill.style.width = "40%";
            loader.style.display = "flex";

            var data = {
                examenId: '
                resultadosJson: JSON.stringify(resultadosAlumnos)
            };

            fetch('
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded',
                },
                body: Object.keys(data).map(key => encodeURIComponent(key) + '=' + encodeURIComponent(data[key])).join('&')
            })
            .then(response => response.json())
            .then(res => {
                if (res.success) {
                    fill.style.width = "100%";
                    location.href = '
                } else {
                    loader.style.display = "none";
                    alert(res.message || "Error al guardar calificaciones.");
                }
            })
            .catch(err => {
                loader.style.display = "none";
                alert("Error de conexión con el servidor.");
            });
        }
    
