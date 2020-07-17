const Joi = require('joi');
const joiSchemas = require('./joiSchemas');
const express = require('express');
const app = express();
const mongoose = require('mongoose');
const WebStatusLog = require('./models/webStatusLog');

app.use(express.json());
mongoose.connect('mongodb://localhost/webStatusLogDB', { useNewUrlParser: true, useUnifiedTopology: true, useFindAndModify: false });


const apiBaseUri = '/api/wsl/v1';

app.get(apiBaseUri + '', (req, res) => {
    res.send('OK');
});

app.get(apiBaseUri + '/logs', (req, res) => {
    if (req.query.guid) {
        WebStatusLog.find({"Guid":req.query.guid}, (err, dbRes) => {
            if (!dbRes) return res.status(404).send('The given GUID was not found.');
            res.send(dbRes);
        });
    }
    else
    {
        WebStatusLog.find({}, function(err, dbRes) {
            if (!dbRes) return res.status(404).send('The logs was not found.');
            res.send(dbRes); 
        });
    }
});

app.post(apiBaseUri + '/logs', (req, res) => {
    const { error } = Joi.validate(req.body, joiSchemas.webStatusLogSchema);
    if (error) return res.status(400).send(error.details[0].message);
    const log = req.body;
    WebStatusLog.insertMany(log,(error,doc) => {
        if (error) return res.status(500).send(error);
        res.send(doc);
    });
});

app.put(apiBaseUri + '/logs/:id', (req, res) => {
    WebStatusLog.findByIdAndUpdate(req.params.id, req.body, (err, dbRes) => {
        if (!dbRes) return res.status(404).send('The log with the given ID was not found.');
        const { error } = Joi.validate(req.body, joiSchemas.webStatusLogSchema);
        if (error) return res.status(400).send(error.details[0].message);
        res.send(dbRes);
    });
});

app.delete(apiBaseUri + '/logs/:id', (req, res) => {
    WebStatusLog.findByIdAndDelete(req.params.id, (err, dbRes) => {
        if (!dbRes) return res.status(404).send('The log with the given ID was not found.');
        res.send(dbRes);
    });
});

app.get(apiBaseUri + '/logs/:id', (req,res) => {
    WebStatusLog.findById(req.params.id, (err, dbRes) => {
        const log = dbRes;
        if (!log) return res.status(404).send('The log with the given ID was not found.');
        res.send(log);
    });
});



const port = process.env.PORT || 3000;
app.listen(port, () => console.log(`Server listening on port ${port}...`));