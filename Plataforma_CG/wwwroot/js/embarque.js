window.descargarQR = function (embarqueId) {
    const img = document.getElementById("imgQR");

    const link = document.createElement("a");
    link.href = img.src;
    link.download = "QR_Embarque_" + embarqueId + ".png";
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

window.imprimirQR = function (qrUrl, token, fecha, embarqueId, Consecutivo) {
    const ventana = window.open("", "_blank");

    ventana.document.write(`
        <html>
        <head>
            <title>Imprimir QR</title>
            <style>
                body { text-align: center; font-family: Arial; }
                img { width: 300px; margin-top: 20px; }
            </style>
        </head>
        <body>
            <h3>QR del Embarque #${Consecutivo}</h3>
            <img id="qrPrint" src="${qrUrl}" />
            <p><strong>Token:</strong> ${token}</p>
            <p><strong>Generado:</strong> ${fecha}</p>

            <script>
                const img = document.getElementById("qrPrint");
                img.onload = function () {
                    window.print();
                    window.close();
                };
            <\/script>
        </body>
        </html>
    `);

    ventana.document.close();
};
