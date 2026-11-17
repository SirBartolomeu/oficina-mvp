namespace Oficina.Billing;

public static class CheckoutTestPage
{
    public const string Html = """
        <!doctype html><html lang="pt-BR"><meta charset="utf-8">
        <meta name="viewport" content="width=device-width"><title>Oficina — Checkout de teste</title>
        <style>body{font:18px system-ui;max-width:640px;margin:4rem auto;padding:1rem}label{display:block;margin:1rem 0}input{display:block;width:100%;padding:.6rem}button,a{padding:.7rem}#pay{display:none}</style>
        <h1>Checkout de teste</h1><p>Use uma tentativa criada pela Saga após aprovação do orçamento e reserva de estoque.</p>
        <form id="form"><label>Tentativa de pagamento<input id="attempt" required></label>
        <label>Chave interna do Billing<input id="key" type="password" autocomplete="off" required></label>
        <button id="load">Consultar tentativa</button></form><p id="status" role="status"></p>
        <p id="total"></p><a id="pay" data-mp-checkout-cta="checkout-pro" rel="noreferrer">Pagar com Mercado Pago (teste)</a>
        <script>
        const form=document.querySelector('#form'),status=document.querySelector('#status'),pay=document.querySelector('#pay');
        form.addEventListener('submit',async e=>{
          e.preventDefault();pay.style.display='none';document.querySelector('#load').disabled=true;status.textContent='Consultando…';
          try {
            const id=document.querySelector('#attempt').value.trim();
            const response=await fetch('/internal/payments/'+encodeURIComponent(id),{headers:{'X-Internal-Token':document.querySelector('#key').value}});
            if(!response.ok)throw new Error('Consulta recusada ('+response.status+'). Confira a tentativa e a chave interna.');
            const payment=await response.json();status.textContent='Estado: '+payment.status;
            document.querySelector('#total').textContent='Total: '+new Intl.NumberFormat('pt-BR',{style:'currency',currency:'BRL'}).format(payment.totalCents/100);
            if(payment.status==='Created' && payment.checkoutUrl){pay.href=payment.checkoutUrl;pay.style.display='inline-block';}
            else if(payment.status==='Requested')status.textContent+=' — aguarde a criação do checkout e consulte novamente.';
          }catch(error){status.textContent=error.message;}finally{document.querySelector('#load').disabled=false;}
        });
        </script></html>
        """;
}
